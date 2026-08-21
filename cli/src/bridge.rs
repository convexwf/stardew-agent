use std::{
    fs::{self, File},
    io::BufWriter,
    path::{Path, PathBuf},
    thread,
    time::{Duration, Instant},
};

use anyhow::{bail, Context, Result};
use serde::Serialize;
use serde_json::Value;
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
        let mut latest = None;
        for entry in fs::read_dir(&self.snapshots)
            .with_context(|| format!("read {}", self.snapshots.display()))?
        {
            let path = entry?.path();
            if path.extension().and_then(|value| value.to_str()) != Some("json") {
                continue;
            }
            let sequence = path
                .file_stem()
                .and_then(|value| value.to_str())
                .and_then(|name| name.strip_prefix("snapshot-"))
                .and_then(|value| value.parse::<u64>().ok());
            let Some(sequence) = sequence else { continue };
            let content = fs::read_to_string(&path)
                .with_context(|| format!("read snapshot {}", path.display()))?;
            let snapshot: Envelope<Value> = serde_json::from_str(&content)
                .with_context(|| format!("parse snapshot {}", path.display()))?;
            if snapshot.message_type != "snapshot" {
                bail!("unexpected message type in {}", path.display());
            }
            if latest.as_ref().map(|(old, _)| sequence > *old).unwrap_or(true) {
                latest = Some((sequence, snapshot));
            }
        }
        Ok(latest.map(|(_, snapshot)| snapshot))
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
