use std::{
    fs::{self, File},
    io::BufWriter,
    path::{Path, PathBuf},
    thread,
    time::{Duration, Instant},
};

use anyhow::{bail, Context, Result};
use serde::Serialize;
use serde_json::{json, Value};
use uuid::Uuid;

use crate::protocol::{action_request, ActionRequestPayload, Envelope};

#[derive(Debug, Clone)]
pub struct Bridge {
    pub root: PathBuf,
    pending: PathBuf,
    processing: PathBuf,
    archive: PathBuf,
    results: PathBuf,
    snapshots: PathBuf,
    errors: PathBuf,
}

impl Bridge {
    pub fn new(root: PathBuf) -> Self {
        Self {
            pending: root.join("actions/pending"),
            processing: root.join("actions/processing"),
            archive: root.join("actions/archive"),
            results: root.join("results"),
            snapshots: root.join("snapshots"),
            errors: root.join("errors"),
            root,
        }
    }

    pub fn ensure_layout(&self) -> Result<()> {
        for path in [
            &self.pending,
            &self.processing,
            &self.archive,
            &self.results,
            &self.snapshots,
            &self.errors,
        ] {
            fs::create_dir_all(path).with_context(|| format!("create {}", path.display()))?;
        }
        Ok(())
    }

    pub fn send_and_wait(&self, payload: ActionRequestPayload, timeout_ms: u64) -> Result<Value> {
        let request_id = Uuid::new_v4().to_string();
        let request = action_request(request_id.clone(), payload);
        let request_path = self.pending.join(format!("{request_id}.json"));
        atomic_write_json(&request_path, &request)?;
        wait_for_result(&self.results, &request_id, timeout_ms)
    }

    pub fn latest_snapshot(&self) -> Result<Option<Envelope<Value>>> {
        let latest_path = self.snapshots.join("snapshot-latest.json");
        if latest_path.is_file() {
            let content = fs::read_to_string(&latest_path)
                .with_context(|| format!("read snapshot {}", latest_path.display()))?;
            let snapshot: Envelope<Value> = serde_json::from_str(&content)
                .with_context(|| format!("parse snapshot {}", latest_path.display()))?;
            if snapshot.message_type == "snapshot" {
                return Ok(Some(snapshot));
            }
        }

        let mut latest = None;
        for entry in fs::read_dir(&self.snapshots)
            .with_context(|| format!("read {}", self.snapshots.display()))?
        {
            let path = entry?.path();
            if path.extension().and_then(|value| value.to_str()) != Some("json") {
                continue;
            }
            let slot = path
                .file_stem()
                .and_then(|value| value.to_str())
                .and_then(|name| name.strip_prefix("snapshot-"))
                .and_then(|value| value.parse::<usize>().ok());
            let Some(_slot) = slot else { continue };
            let content = fs::read_to_string(&path)
                .with_context(|| format!("read snapshot {}", path.display()))?;
            let snapshot: Envelope<Value> = serde_json::from_str(&content)
                .with_context(|| format!("parse snapshot {}", path.display()))?;
            if snapshot.message_type != "snapshot" {
                bail!("unexpected message type in {}", path.display());
            }
            let sequence = snapshot
                .payload
                .get("snapshot_sequence")
                .and_then(Value::as_u64)
                .unwrap_or(0);
            if latest.as_ref().map(|(old, _)| sequence > *old).unwrap_or(true) {
                latest = Some((sequence, snapshot));
            }
        }
        Ok(latest.map(|(_, snapshot)| snapshot))
    }

    pub fn write_snapshot<T: Serialize>(
        &self,
        snapshot_sequence: u64,
        latest_write_sequence: u64,
        snapshot: &T,
        max_history: usize,
    ) -> Result<()> {
        if max_history == 0 {
            bail!("snapshot history limit must be greater than zero");
        }

        normalize_snapshot_slots(&self.snapshots, max_history)?;
        let index = next_snapshot_index(&self.snapshots, max_history)?;
        let mut value = serde_json::to_value(snapshot)?;
        let payload = value
            .get_mut("payload")
            .and_then(Value::as_object_mut)
            .context("snapshot payload must be a JSON object")?;
        payload.insert("latest_write_sequence".to_owned(), json!(latest_write_sequence));
        payload.insert("snapshot_sequence".to_owned(), json!(snapshot_sequence));
        payload.insert("snapshot_index".to_owned(), json!(index));

        // A slot is intentionally reused. Remove-and-rename keeps this path
        // working on Windows, where rename does not replace an existing file.
        replace_json(&self.snapshots.join(format!("snapshot-{index}.json")), &value)?;
        replace_json(&self.snapshots.join("snapshot-latest.json"), &value)
    }
}

pub fn atomic_write_json<T: Serialize>(path: &Path, value: &T) -> Result<()> {
    let parent = path.parent().context("JSON path has no parent")?;
    fs::create_dir_all(parent).with_context(|| format!("create {}", parent.display()))?;
    let file_name = path.file_name().context("JSON path has no file name")?;
    let temp_path = parent.join(format!(".{}.{}.tmp", file_name.to_string_lossy(), Uuid::new_v4()));
    let file = File::create(&temp_path)
        .with_context(|| format!("create temporary file {}", temp_path.display()))?;
    let mut writer = BufWriter::new(file);
    serde_json::to_writer_pretty(&mut writer, value)?;
    std::io::Write::flush(&mut writer)?;
    writer.get_ref().sync_all()?;
    drop(writer);
    fs::rename(&temp_path, path).with_context(|| format!("rename to {}", path.display()))?;
    Ok(())
}

pub fn replace_json<T: Serialize>(path: &Path, value: &T) -> Result<()> {
    let parent = path.parent().context("JSON path has no parent")?;
    fs::create_dir_all(parent).with_context(|| format!("create {}", parent.display()))?;
    let file_name = path.file_name().context("JSON path has no file name")?;
    let temp_path = parent.join(format!(".{}.{}.tmp", file_name.to_string_lossy(), Uuid::new_v4()));
    let file = File::create(&temp_path)
        .with_context(|| format!("create temporary file {}", temp_path.display()))?;
    let mut writer = BufWriter::new(file);
    serde_json::to_writer_pretty(&mut writer, value)?;
    std::io::Write::flush(&mut writer)?;
    writer.get_ref().sync_all()?;
    drop(writer);
    if path.exists() {
        fs::remove_file(path).with_context(|| format!("remove {}", path.display()))?;
    }
    fs::rename(&temp_path, path).with_context(|| format!("rename to {}", path.display()))?;
    Ok(())
}

pub fn normalize_snapshot_slots(snapshots_dir: &Path, max_history: usize) -> Result<()> {
    for entry in fs::read_dir(snapshots_dir)
        .with_context(|| format!("read {}", snapshots_dir.display()))?
    {
        let path = entry?.path();
        if path.file_name().and_then(|value| value.to_str()) == Some("snapshot-latest.json") {
            continue;
        }
        if path.extension().and_then(|value| value.to_str()) != Some("json") {
            continue;
        }
        let Some(index) = path
            .file_stem()
            .and_then(|value| value.to_str())
            .and_then(|name| name.strip_prefix("snapshot-"))
            .and_then(|value| value.parse::<usize>().ok())
        else {
            continue;
        };
        if index >= max_history {
            fs::remove_file(&path).with_context(|| format!("remove {}", path.display()))?;
        }
    }
    Ok(())
}

fn next_snapshot_index(snapshots_dir: &Path, max_history: usize) -> Result<usize> {
    let latest_path = snapshots_dir.join("snapshot-latest.json");
    if latest_path.is_file() {
        let content = fs::read_to_string(&latest_path)?;
        if let Ok(snapshot) = serde_json::from_str::<Envelope<Value>>(&content) {
            if let Some(index) = snapshot
                .payload
                .get("snapshot_index")
                .and_then(Value::as_i64)
                .filter(|index| *index >= 0 && (*index as usize) < max_history)
            {
                return Ok((index as usize + 1) % max_history);
            }
        }
    }

    let mut newest = None;
    for entry in fs::read_dir(snapshots_dir)? {
        let path = entry?.path();
        let Some(index) = path
            .file_stem()
            .and_then(|value| value.to_str())
            .and_then(|name| name.strip_prefix("snapshot-"))
            .and_then(|value| value.parse::<usize>().ok())
            .filter(|index| *index < max_history)
        else {
            continue;
        };
        let Ok(content) = fs::read_to_string(&path) else { continue };
        let Ok(snapshot) = serde_json::from_str::<Envelope<Value>>(&content) else { continue };
        let sequence = snapshot
            .payload
            .get("snapshot_sequence")
            .and_then(Value::as_u64)
            .unwrap_or(0);
        if newest.map(|(old, _)| sequence > old).unwrap_or(true) {
            newest = Some((sequence, index));
        }
    }
    Ok(newest.map(|(_, index)| (index + 1) % max_history).unwrap_or(0))
}

fn wait_for_result(results_dir: &Path, request_id: &str, timeout_ms: u64) -> Result<Value> {
    let result_path = results_dir.join(format!("{request_id}.json"));
    let deadline = Instant::now() + Duration::from_millis(timeout_ms);
    loop {
        if result_path.is_file() {
            let content = fs::read_to_string(&result_path)
                .with_context(|| format!("read result {}", result_path.display()))?;
            let result: Value = serde_json::from_str(&content)
                .with_context(|| format!("parse result {}", result_path.display()))?;
            return Ok(result);
        }
        if Instant::now() >= deadline {
            bail!("timeout waiting for request {request_id}");
        }
        thread::sleep(Duration::from_millis(50));
    }
}
