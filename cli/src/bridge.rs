use std::{
    fs::{self, File},
    io::BufWriter,
    path::{Path, PathBuf},
    thread,
    time::{Duration, Instant, SystemTime, UNIX_EPOCH},
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
            if let Some(snapshot) = read_snapshot_file_best_effort(&latest_path) {
                return Ok(Some(snapshot));
            }
        }

        let mut latest = None;
        for path in snapshot_paths(&self.snapshots)? {
            let Some(snapshot) = read_snapshot_file_best_effort(&path) else {
                continue;
            };
            let sequence = snapshot_sequence(&snapshot);
            if latest.as_ref().map(|(old, _)| sequence > *old).unwrap_or(true) {
                latest = Some((sequence, snapshot));
            }
        }
        Ok(latest.map(|(_, snapshot)| snapshot))
    }

    pub fn snapshot_slots(&self) -> Result<Vec<Envelope<Value>>> {
        let mut snapshots = Vec::new();
        for path in snapshot_paths(&self.snapshots)? {
            if let Some(snapshot) = read_snapshot_file_best_effort(&path) {
                snapshots.push(snapshot);
            }
        }
        snapshots.sort_by_key(snapshot_sequence);
        Ok(snapshots)
    }

    pub fn snapshot_slot(&self, index: usize) -> Result<Option<Envelope<Value>>> {
        let path = self.snapshots.join(format!("snapshot-{index}.json"));
        if !path.is_file() {
            return Ok(None);
        }
        read_snapshot_file(&path)
    }

    pub fn read_result(&self, request_id: &str) -> Result<Option<Value>> {
        read_json_file(&self.results.join(format!("{request_id}.json")))
    }

    pub fn read_request(&self, request_id: &str) -> Result<Option<Value>> {
        let file_name = format!("{request_id}.json");
        for (location, directory) in [
            ("pending", &self.pending),
            ("processing", &self.processing),
            ("archive", &self.archive),
        ] {
            let path = directory.join(&file_name);
            if let Some(content) = read_json_file(&path)? {
                return Ok(Some(json!({
                    "request_id": request_id,
                    "location": location,
                    "path": path,
                    "content": content,
                })));
            }
        }

        for entry in fs::read_dir(&self.errors)
            .with_context(|| format!("read {}", self.errors.display()))?
        {
            let path = entry?.path();
            let Some(name) = path.file_name().and_then(|value| value.to_str()) else {
                continue;
            };
            if !name.starts_with(request_id) || path.extension().and_then(|value| value.to_str()) != Some("json") {
                continue;
            }
            if let Some(content) = read_json_file(&path)? {
                return Ok(Some(json!({
                    "request_id": request_id,
                    "location": "errors",
                    "path": path,
                    "content": content,
                })));
            }
        }
        Ok(None)
    }

    pub fn doctor(&self) -> Result<Value> {
        self.ensure_layout()?;
        let directories = [
            ("pending", self.pending.is_dir()),
            ("processing", self.processing.is_dir()),
            ("archive", self.archive.is_dir()),
            ("results", self.results.is_dir()),
            ("snapshots", self.snapshots.is_dir()),
            ("errors", self.errors.is_dir()),
        ];
        let latest = self.latest_snapshot()?;
        let snapshot_count = self.snapshot_slots()?.len();
        let temp_file_count = count_temp_files(&self.root)?;
        Ok(json!({
            "bridge_dir": self.root,
            "directories": directories.iter().map(|(name, exists)| json!({"name": name, "exists": exists})).collect::<Vec<_>>(),
            "layout_ready": directories.iter().all(|(_, exists)| *exists),
            "latest_snapshot_available": latest.is_some(),
            "latest_write_sequence": latest.as_ref().and_then(|snapshot| snapshot.payload.get("latest_write_sequence")).and_then(Value::as_u64),
            "snapshot_count": snapshot_count,
            "temporary_file_count": temp_file_count,
        }))
    }

    pub fn cleanup(&self, older_than_seconds: u64, dry_run: bool) -> Result<Value> {
        let cutoff = SystemTime::now()
            .checked_sub(Duration::from_secs(older_than_seconds))
            .unwrap_or(UNIX_EPOCH);
        let mut reports = Vec::new();
        for (name, directory) in [
            ("results", &self.results),
            ("archive", &self.archive),
            ("errors", &self.errors),
        ] {
            let mut scanned = 0u64;
            let mut eligible = 0u64;
            let mut removed = 0u64;
            for entry in fs::read_dir(directory)
                .with_context(|| format!("read {}", directory.display()))?
            {
                let path = entry?.path();
                if path.extension().and_then(|value| value.to_str()) != Some("json") {
                    continue;
                }
                scanned += 1;
                let old = path
                    .metadata()
                    .and_then(|metadata| metadata.modified())
                    .map(|modified| modified < cutoff)
                    .unwrap_or(false);
                if !old {
                    continue;
                }
                eligible += 1;
                if !dry_run && fs::remove_file(&path).is_ok() {
                    removed += 1;
                }
            }
            reports.push(json!({
                "directory": name,
                "scanned": scanned,
                "eligible": eligible,
                "removed": removed,
            }));
        }
        Ok(json!({
            "dry_run": dry_run,
            "older_than_seconds": older_than_seconds,
            "directories": reports,
        }))
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

        replace_json(&self.snapshots.join(format!("snapshot-{index}.json")), &value)?;
        replace_json(&self.snapshots.join("snapshot-latest.json"), &value)
    }
}

pub fn atomic_write_json<T: Serialize>(path: &Path, value: &T) -> Result<()> {
    let parent = path.parent().context("JSON path has no parent")?;
    fs::create_dir_all(parent).with_context(|| format!("create {}", parent.display()))?;
    let file_name = path.file_name().context("JSON path has no file name")?;
    let temp_path = parent.join(format!(".{}.{}.tmp", file_name.to_string_lossy(), Uuid::new_v4()));
    let result = (|| {
        let file = File::create(&temp_path)
            .with_context(|| format!("create temporary file {}", temp_path.display()))?;
        let mut writer = BufWriter::new(file);
        serde_json::to_writer_pretty(&mut writer, value)?;
        std::io::Write::flush(&mut writer)?;
        writer.get_ref().sync_all()?;
        drop(writer);
        fs::rename(&temp_path, path).with_context(|| format!("rename to {}", path.display()))?;
        Ok(())
    })();
    if temp_path.exists() {
        let _ = fs::remove_file(&temp_path);
    }
    result
}

pub fn replace_json<T: Serialize>(path: &Path, value: &T) -> Result<()> {
    let parent = path.parent().context("JSON path has no parent")?;
    fs::create_dir_all(parent).with_context(|| format!("create {}", parent.display()))?;
    let file_name = path.file_name().context("JSON path has no file name")?;
    let temp_path = parent.join(format!(".{}.{}.tmp", file_name.to_string_lossy(), Uuid::new_v4()));
    let result = (|| {
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
    })();
    if temp_path.exists() {
        let _ = fs::remove_file(&temp_path);
    }
    result
}

fn read_json_file(path: &Path) -> Result<Option<Value>> {
    if !path.is_file() {
        return Ok(None);
    }
    let content = fs::read_to_string(path).with_context(|| format!("read {}", path.display()))?;
    Ok(Some(serde_json::from_str(&content).with_context(|| format!("parse {}", path.display()))?))
}

fn read_snapshot_file(path: &Path) -> Result<Option<Envelope<Value>>> {
    let Some(value) = read_json_file(path)? else {
        return Ok(None);
    };
    let snapshot: Envelope<Value> = serde_json::from_value(value)
        .with_context(|| format!("parse snapshot {}", path.display()))?;
    if snapshot.message_type != "snapshot" {
        return Ok(None);
    }
    Ok(Some(snapshot))
}

fn read_snapshot_file_best_effort(path: &Path) -> Option<Envelope<Value>> {
    read_snapshot_file(path).ok().flatten()
}

fn snapshot_paths(snapshots_dir: &Path) -> Result<Vec<PathBuf>> {
    let mut paths = Vec::new();
    for entry in fs::read_dir(snapshots_dir)
        .with_context(|| format!("read {}", snapshots_dir.display()))?
    {
        let path = entry?.path();
        if path.file_name().and_then(|value| value.to_str()) == Some("snapshot-latest.json") {
            continue;
        }
        let Some(name) = path.file_stem().and_then(|value| value.to_str()) else {
            continue;
        };
        if path.extension().and_then(|value| value.to_str()) == Some("json")
            && name.strip_prefix("snapshot-").and_then(|value| value.parse::<usize>().ok()).is_some()
        {
            paths.push(path);
        }
    }
    Ok(paths)
}

fn snapshot_sequence(snapshot: &Envelope<Value>) -> u64 {
    snapshot
        .payload
        .get("snapshot_sequence")
        .and_then(Value::as_u64)
        .unwrap_or(0)
}

fn count_temp_files(root: &Path) -> Result<u64> {
    let mut count = 0;
    for entry in walk_files(root)? {
        if entry.extension().and_then(|value| value.to_str()) == Some("tmp") {
            count += 1;
        }
    }
    Ok(count)
}

fn walk_files(root: &Path) -> Result<Vec<PathBuf>> {
    let mut files = Vec::new();
    if !root.is_dir() {
        return Ok(files);
    }
    for entry in fs::read_dir(root)? {
        let path = entry?.path();
        if path.is_dir() {
            files.extend(walk_files(&path)?);
        } else {
            files.push(path);
        }
    }
    Ok(files)
}

pub fn normalize_snapshot_slots(snapshots_dir: &Path, max_history: usize) -> Result<()> {
    for path in snapshot_paths(snapshots_dir)? {
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
    if let Some(latest) = read_snapshot_file_best_effort(&latest_path) {
        if let Some(index) = latest
            .payload
            .get("snapshot_index")
            .and_then(Value::as_i64)
            .filter(|index| *index >= 0 && (*index as usize) < max_history)
        {
            return Ok((index as usize + 1) % max_history);
        }
    }

    let mut newest = None;
    for path in snapshot_paths(snapshots_dir)? {
        let Some(index) = path
            .file_stem()
            .and_then(|value| value.to_str())
            .and_then(|name| name.strip_prefix("snapshot-"))
            .and_then(|value| value.parse::<usize>().ok())
            .filter(|index| *index < max_history)
        else {
            continue;
        };
        let Some(snapshot) = read_snapshot_file_best_effort(&path) else {
            continue;
        };
        let sequence = snapshot_sequence(&snapshot);
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
