use std::{
    fs,
    path::{Path, PathBuf},
    thread,
    time::{Duration, Instant},
};

use anyhow::{Context, Result};
use serde_json::{json, Value};

use crate::{
    bridge::{atomic_write_json, Bridge},
    protocol::{now_ms, ActionRequest, ActionRequestPayload, Direction, Envelope, SCHEMA_VERSION},
};

pub fn run(bridge_dir: PathBuf, interval_ms: u64, once: bool) -> Result<()> {
    let bridge = Bridge::new(bridge_dir);
    bridge.ensure_layout()?;
    let pending = bridge.root.join("actions/pending");
    let processing = bridge.root.join("actions/processing");
    let archive = bridge.root.join("actions/archive");
    let results = bridge.root.join("results");
    let mut tile = (64_i32, 15_i32);
    let mut sequence = 0_u64;
    let mut last_snapshot = Instant::now() - Duration::from_millis(interval_ms);

    loop {
        let processed = process_pending(
            &pending,
            &processing,
            &archive,
            &results,
            &mut tile,
            sequence,
        )?;

        if last_snapshot.elapsed() >= Duration::from_millis(interval_ms.max(50)) {
            sequence += 1;
            write_snapshot(&bridge, sequence, tile)?;
            last_snapshot = Instant::now();
        }

        if once && processed {
            return Ok(());
        }
        thread::sleep(Duration::from_millis(50));
    }
}

fn process_pending(
    pending: &Path,
    processing: &Path,
    archive: &Path,
    results: &Path,
    tile: &mut (i32, i32),
    mod_tick: u64,
) -> Result<bool> {
    let Some(path) = fs::read_dir(pending)?
        .filter_map(|entry| entry.ok().map(|item| item.path()))
        .filter(|path| path.extension().and_then(|value| value.to_str()) == Some("json"))
        .min_by_key(|path| path.file_name().map(|value| value.to_os_string()))
    else {
        return Ok(false);
    };

    let file_name = path.file_name().context("pending file has no name")?;
    let processing_path = processing.join(file_name);
    if fs::rename(&path, &processing_path).is_err() {
        return Ok(false);
    }

    let request_id = file_name.to_string_lossy().trim_end_matches(".json").to_owned();
    let result = match fs::read_to_string(&processing_path) {
        Ok(content) => match serde_json::from_str::<ActionRequest>(&content) {
            Ok(request) => execute_request(request, tile, mod_tick),
            Err(error) => failure(&request_id, "invalid_request", error.to_string()),
        },
        Err(error) => failure(&request_id, "read_error", error.to_string()),
    };

    atomic_write_json(&results.join(format!("{request_id}.json")), &result)?;
    let _ = fs::rename(&processing_path, archive.join(file_name));
    Ok(true)
}

fn execute_request(
    request: ActionRequest,
    tile: &mut (i32, i32),
    mod_tick: u64,
) -> Envelope<Value> {
    let request_id = request.request_id.unwrap_or_else(|| "unknown".to_owned());
    let payload = match request.payload {
        ActionRequestPayload::Ping => json!({
            "status": "succeeded",
            "action": "ping",
            "mod_tick": mod_tick,
            "world_ready": true,
        }),
        ActionRequestPayload::MoveRelative { direction, ticks } => {
            let before = json!({"x": tile.0, "y": tile.1});
            let distance = (ticks / 5).max(1) as i32;
            match direction {
                Direction::Up => tile.1 -= distance,
                Direction::Down => tile.1 += distance,
                Direction::Left => tile.0 -= distance,
                Direction::Right => tile.0 += distance,
            }
            json!({
                "status": "succeeded",
                "action": "move_relative",
                "direction": direction.as_str(),
                "ticks": ticks,
                "before_tile": before,
                "after_tile": {"x": tile.0, "y": tile.1},
                "moved": true,
                "world_ready": true,
            })
        }
    };
    Envelope {
        schema_version: SCHEMA_VERSION.to_owned(),
        message_type: "action.result".to_owned(),
        request_id: Some(request_id),
        created_at_ms: now_ms(),
        payload,
    }
}

fn failure(request_id: &str, code: &str, message: String) -> Envelope<Value> {
    Envelope {
        schema_version: SCHEMA_VERSION.to_owned(),
        message_type: "action.result".to_owned(),
        request_id: Some(request_id.to_owned()),
        created_at_ms: now_ms(),
        payload: json!({
            "status": "failed",
            "error": {"code": code, "message": message},
        }),
    }
}

fn write_snapshot(bridge: &Bridge, sequence: u64, tile: (i32, i32)) -> Result<()> {
    let snapshot = Envelope {
        schema_version: SCHEMA_VERSION.to_owned(),
        message_type: "snapshot".to_owned(),
        request_id: None::<String>,
        created_at_ms: now_ms(),
        payload: json!({
            "sequence": sequence,
            "mod_version": "0.1.0-fake",
            "game_tick": sequence * 60,
            "world_ready": true,
            "game": {"year": 1, "season": "spring", "day": 1, "time": 900},
            "player": {"location": "Farm", "tile": {"x": tile.0, "y": tile.1}},
        }),
    };
    atomic_write_json(
        &bridge.root.join("snapshots").join(format!("snapshot-{sequence}.json")),
        &snapshot,
    )
}
