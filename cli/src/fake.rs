use std::{
    fs,
    path::{Path, PathBuf},
    thread,
    time::{Duration, Instant},
};

use anyhow::{Context, Result};
use serde_json::{json, Value};

use crate::{
    bridge::{atomic_write_json, replace_json, Bridge},
    protocol::{
        now_ms, ActionRequest, ActionRequestPayload, Direction, Envelope, COMPANION_ID,
        SCHEMA_VERSION,
    },
};

pub fn run(
    bridge_dir: PathBuf,
    latest_interval_ms: u64,
    snapshot_history_interval_ms: u64,
    once: bool,
    snapshot_history_limit: usize,
) -> Result<()> {
    if latest_interval_ms == 0 || snapshot_history_interval_ms == 0 {
        anyhow::bail!("snapshot intervals must be greater than zero");
    }
    if !(1..=1024).contains(&snapshot_history_limit) {
        anyhow::bail!("snapshot history limit must be between 1 and 1024");
    }

    let bridge = Bridge::new(bridge_dir);
    bridge.ensure_layout()?;
    let pending = bridge.root.join("actions/pending");
    let processing = bridge.root.join("actions/processing");
    let archive = bridge.root.join("actions/archive");
    let results = bridge.root.join("results");
    let player_tile = (64_i32, 15_i32);
    let latest = bridge.latest_snapshot()?;
    let mut companion_tile = latest
        .as_ref()
        .and_then(|snapshot| snapshot.payload.get("companion"))
        .and_then(|companion| companion.get("tile"))
        .and_then(|tile| {
            Some((
                tile.get("x")?.as_i64()? as i32,
                tile.get("y")?.as_i64()? as i32,
            ))
        })
        .unwrap_or((65_i32, 15_i32));
    let mut state = FakeState {
        location: latest
            .as_ref()
            .and_then(|snapshot| snapshot.payload.get("companion"))
            .and_then(|companion| companion.get("location"))
            .and_then(Value::as_str)
            .unwrap_or("Farm")
            .to_owned(),
        facing_direction: latest
            .as_ref()
            .and_then(|snapshot| snapshot.payload.get("companion"))
            .and_then(|companion| companion.get("facing_direction"))
            .and_then(Value::as_str)
            .unwrap_or("down")
            .to_owned(),
        auto_combat: latest
            .as_ref()
            .and_then(|snapshot| snapshot.payload.get("companion"))
            .and_then(|companion| companion.get("auto_combat"))
            .and_then(Value::as_bool)
            .unwrap_or(false),
        follow: None,
    };
    let mut latest_write_sequence = latest
        .as_ref()
        .and_then(|snapshot| snapshot.payload.get("latest_write_sequence"))
        .and_then(Value::as_u64)
        .unwrap_or(0);
    let mut snapshot_sequence = latest
        .as_ref()
        .and_then(|snapshot| snapshot.payload.get("snapshot_sequence"))
        .and_then(Value::as_u64)
        .unwrap_or(0);
    let mut latest_snapshot_index = latest
        .as_ref()
        .and_then(|snapshot| snapshot.payload.get("snapshot_index"))
        .and_then(Value::as_i64)
        .filter(|index| *index >= 0 && (*index as usize) < snapshot_history_limit)
        .map(|index| index as usize)
        .unwrap_or(usize::MAX);
    let mut last_latest = Instant::now() - Duration::from_millis(latest_interval_ms);
    let mut last_history =
        Instant::now() - Duration::from_millis(snapshot_history_interval_ms);

    loop {
        let processed = process_pending(
            &pending,
            &processing,
            &archive,
            &results,
            &mut companion_tile,
            &mut state,
            latest_write_sequence,
        )?;

        if last_latest.elapsed() >= Duration::from_millis(latest_interval_ms.max(50)) {
            latest_write_sequence += 1;
            write_latest_snapshot(
                &bridge,
                snapshot_sequence,
                latest_write_sequence,
                player_tile,
                companion_tile,
                &state,
                latest_snapshot_index,
            )?;
            last_latest = Instant::now();
        }

        if last_history.elapsed()
            >= Duration::from_millis(snapshot_history_interval_ms.max(50))
        {
            snapshot_sequence += 1;
            latest_write_sequence += 1;
            write_history_snapshot(
                &bridge,
                snapshot_sequence,
                latest_write_sequence,
                player_tile,
                companion_tile,
                &state,
                snapshot_history_limit,
            )?;
            latest_snapshot_index = bridge
                .latest_snapshot()?
                .and_then(|snapshot| snapshot.payload.get("snapshot_index").cloned())
                .and_then(|index| index.as_i64())
                .filter(|index| *index >= 0)
                .map(|index| index as usize)
                .unwrap_or(latest_snapshot_index);
            last_history = Instant::now();
        }

        if once && processed {
            return Ok(());
        }
        thread::sleep(Duration::from_millis(50));
    }
}

struct FakeState {
    location: String,
    facing_direction: String,
    auto_combat: bool,
    follow: Option<FakeFollow>,
}

struct FakeFollow {
    request_id: String,
    target_actor_id: String,
    distance: u32,
    state: String,
    warp_count: u32,
}

fn process_pending(
    pending: &Path,
    processing: &Path,
    archive: &Path,
    results: &Path,
    companion_tile: &mut (i32, i32),
    state: &mut FakeState,
    mod_tick: u64,
) -> Result<bool> {
    let paths = fs::read_dir(pending)?
        .filter_map(|entry| entry.ok().map(|item| item.path()))
        .filter(|path| path.extension().and_then(|value| value.to_str()) == Some("json"))
        .collect::<Vec<_>>();
    let Some(path) = paths
        .iter()
        .find(|path| is_cancel_request(path))
        .cloned()
        .or_else(|| paths.into_iter().min_by_key(|path| path.file_name().map(|value| value.to_os_string())))
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
            Ok(request) => {
                if matches!(&request.payload, ActionRequestPayload::Cancel { .. }) {
                    Some(execute_cancel_request(request, pending, processing, archive, results, state)?)
                } else if matches!(&request.payload, ActionRequestPayload::Follow { .. }) {
                    start_follow_request(request, state)?
                } else {
                    Some(validate_and_execute(request, companion_tile, state, mod_tick))
                }
            }
            Err(error) => Some(failure(&request_id, None, None, "invalid_request", error.to_string())),
        },
        Err(error) => Some(failure(&request_id, None, None, "read_error", error.to_string())),
    };

    if let Some(result) = result {
        atomic_write_json(&results.join(format!("{request_id}.json")), &result)?;
    }
    let _ = fs::rename(&processing_path, archive.join(file_name));
    Ok(true)
}

fn is_cancel_request(path: &Path) -> bool {
    fs::read_to_string(path)
        .ok()
        .and_then(|content| serde_json::from_str::<ActionRequest>(&content).ok())
        .map(|request| matches!(request.payload, ActionRequestPayload::Cancel { .. }))
        .unwrap_or(false)
}

fn execute_cancel_request(
    request: ActionRequest,
    pending: &Path,
    processing: &Path,
    archive: &Path,
    results: &Path,
    state: &mut FakeState,
) -> Result<Envelope<Value>> {
    let request_id = request.request_id.as_deref().unwrap_or("unknown");
    let ActionRequestPayload::Cancel {
        actor_id,
        target_request_id,
    } = request.payload
    else {
        unreachable!();
    };
    if actor_id != COMPANION_ID {
        return Ok(failure(
            request_id,
            Some("cancel"),
            Some(&actor_id),
            "unsupported_actor",
            format!("only {COMPANION_ID} is available in this demo"),
        ));
    }
    if target_request_id.trim().is_empty() {
        return Ok(failure(
            request_id,
            Some("cancel"),
            Some(&actor_id),
            "invalid_target_request",
            "target_request_id must not be empty".to_owned(),
        ));
    }

    if state
        .follow
        .as_ref()
        .map(|follow| follow.request_id == target_request_id.trim())
        .unwrap_or(false)
    {
        let follow = state.follow.take().expect("follow state was checked above");
        let target_result = cancelled(
            &follow.request_id,
            "follow",
            COMPANION_ID,
            "cancelled_by_request",
            "action cancelled by a cancel request",
        );
        atomic_write_json(&results.join(format!("{}.json", follow.request_id)), &target_result)?;
        return Ok(succeeded(
            request_id,
            "cancel",
            &actor_id,
            json!({
                "target_request_id": target_request_id,
                "target_action": "follow",
                "target_status": "cancelled",
                "cancelled": true
            }),
        ));
    }

    let target_path = pending.join(format!("{}.json", target_request_id.trim()));
    if target_path.is_file() {
        let target_file_name = target_path.file_name().context("target request has no name")?;
        let target_processing_path = processing.join(target_file_name);
        fs::rename(&target_path, &target_processing_path)
            .with_context(|| format!("claim target request {}", target_request_id))?;
        let target_content = fs::read_to_string(&target_processing_path)?;
        let target: ActionRequest = serde_json::from_str(&target_content)?;
        let target_action = target.payload.action_name();
        let target_actor = action_actor_id(&target.payload);
        let target_result = cancelled(
            target.request_id.as_deref().unwrap_or(target_request_id.trim()),
            target_action,
            target_actor,
            "cancelled_before_start",
            "action cancelled before execution",
        );
        atomic_write_json(
            &results.join(format!("{}.json", target_request_id.trim())),
            &target_result,
        )?;
        fs::rename(&target_processing_path, archive.join(target_file_name))?;
        return Ok(succeeded(
            request_id,
            "cancel",
            &actor_id,
            json!({
                "target_request_id": target_request_id,
                "target_action": target_action,
                "target_status": "cancelled",
                "cancelled": true
            }),
        ));
    }

    if results.join(format!("{}.json", target_request_id.trim())).is_file() {
        return Ok(failure(
            request_id,
            Some("cancel"),
            Some(&actor_id),
            "request_completed",
            format!("request has already completed: {}", target_request_id.trim()),
        ));
    }

    Ok(failure(
        request_id,
        Some("cancel"),
        Some(&actor_id),
        "request_not_found",
        format!("request not found: {}", target_request_id.trim()),
    ))
}

fn start_follow_request(request: ActionRequest, state: &mut FakeState) -> Result<Option<Envelope<Value>>> {
    let request_id = request.request_id.as_deref().unwrap_or("unknown");
    if request.schema_version != SCHEMA_VERSION {
        return Ok(Some(failure(
            request_id,
            None,
            None,
            "unsupported_schema",
            format!("unsupported schema version: {}", request.schema_version),
        )));
    }
    if request.message_type != "action.request" || request.request_id.is_none() {
        return Ok(Some(failure(
            request_id,
            None,
            None,
            "invalid_request",
            "request envelope is invalid".to_owned(),
        )));
    }

    let ActionRequestPayload::Follow {
        actor_id,
        target_actor_id,
        distance,
    } = request.payload
    else {
        unreachable!();
    };
    if actor_id != COMPANION_ID {
        return Ok(Some(failure(
            request_id,
            Some("follow"),
            Some(&actor_id),
            "unsupported_actor",
            format!("only {COMPANION_ID} is available in this demo"),
        )));
    }
    if target_actor_id != "player" {
        return Ok(Some(failure(
            request_id,
            Some("follow"),
            Some(&actor_id),
            "unsupported_follow_target",
            "the only supported follow target is player".to_owned(),
        )));
    }
    if !(1..=8).contains(&distance) {
        return Ok(Some(failure(
            request_id,
            Some("follow"),
            Some(&actor_id),
            "invalid_distance",
            "distance must be between 1 and 8".to_owned(),
        )));
    }
    if state.follow.is_some() {
        return Ok(Some(failure(
            request_id,
            Some("follow"),
            Some(&actor_id),
            "busy",
            "the companion is already following the player".to_owned(),
        )));
    }

    state.follow = Some(FakeFollow {
        request_id: request_id.to_owned(),
        target_actor_id,
        distance,
        state: "following".to_owned(),
        warp_count: 0,
    });
    Ok(None)
}

fn validate_and_execute(
    request: ActionRequest,
    companion_tile: &mut (i32, i32),
    state: &mut FakeState,
    mod_tick: u64,
) -> Envelope<Value> {
    let request_id = request.request_id.as_deref().unwrap_or("unknown");
    if request.schema_version != SCHEMA_VERSION {
        return failure(
            request_id,
            None,
            None,
            "unsupported_schema",
            format!("unsupported schema version: {}", request.schema_version),
        );
    }
    if request.message_type != "action.request" || request.request_id.is_none() {
        return failure(
            request_id,
            None,
            None,
            "invalid_request",
            "request envelope is invalid".to_owned(),
        );
    }

    let actor_id = action_actor_id(&request.payload);
    let action = action_name(&request.payload);
    if actor_id != COMPANION_ID {
        return failure(
            request_id,
            Some(action),
            Some(actor_id),
            "unsupported_actor",
            format!("only {COMPANION_ID} is available in this demo"),
        );
    }
    if let ActionRequestPayload::MoveRelative { ticks, .. } = &request.payload {
        if !(1..=30).contains(ticks) {
            return failure(
                request_id,
                Some(action),
                Some(actor_id),
                "invalid_ticks",
                "ticks must be between 1 and 30".to_owned(),
            );
        }
    }
    if let ActionRequestPayload::Observe { radius, .. } = &request.payload {
        if !(1..=16).contains(radius) {
            return failure(
                request_id,
                Some(action),
                Some(actor_id),
                "invalid_radius",
                "radius must be between 1 and 16".to_owned(),
            );
        }
    }
    if state.follow.is_some() {
        return failure(
            request_id,
            Some(action),
            Some(actor_id),
            "busy",
            "the companion is already following the player".to_owned(),
        );
    }

    execute_request(request, companion_tile, state, mod_tick)
}

fn action_actor_id(payload: &ActionRequestPayload) -> &str {
    match payload {
        ActionRequestPayload::Ping { actor_id }
        | ActionRequestPayload::MoveRelative { actor_id, .. }
        | ActionRequestPayload::MoveTo { actor_id, .. }
        | ActionRequestPayload::Follow { actor_id, .. }
        | ActionRequestPayload::StartMode { actor_id, .. }
        | ActionRequestPayload::FaceDirection { actor_id, .. }
        | ActionRequestPayload::UseTool { actor_id, .. }
        | ActionRequestPayload::Interact { actor_id, .. }
        | ActionRequestPayload::WarpTo { actor_id, .. }
        | ActionRequestPayload::Observe { actor_id, .. }
        | ActionRequestPayload::GetInventory { actor_id }
        | ActionRequestPayload::Attack { actor_id }
        | ActionRequestPayload::CastFishingRod { actor_id }
        | ActionRequestPayload::SetAutoCombat { actor_id, .. }
        | ActionRequestPayload::EatItem { actor_id, .. }
        | ActionRequestPayload::Say { actor_id, .. }
        | ActionRequestPayload::Bubble { actor_id, .. }
        | ActionRequestPayload::Cancel { actor_id, .. } => actor_id,
    }
}

fn action_name(payload: &ActionRequestPayload) -> &'static str {
    match payload {
        ActionRequestPayload::Ping { .. } => "ping",
        ActionRequestPayload::MoveRelative { .. } => "move_relative",
        ActionRequestPayload::MoveTo { .. } => "move_to",
        ActionRequestPayload::Follow { .. } => "follow",
        ActionRequestPayload::StartMode { .. } => "start_mode",
        ActionRequestPayload::FaceDirection { .. } => "face_direction",
        ActionRequestPayload::UseTool { .. } => "use_tool",
        ActionRequestPayload::Interact { .. } => "interact",
        ActionRequestPayload::WarpTo { .. } => "warp_to",
        ActionRequestPayload::Observe { .. } => "observe",
        ActionRequestPayload::GetInventory { .. } => "get_inventory",
        ActionRequestPayload::Attack { .. } => "attack",
        ActionRequestPayload::CastFishingRod { .. } => "cast_fishing_rod",
        ActionRequestPayload::SetAutoCombat { .. } => "set_auto_combat",
        ActionRequestPayload::EatItem { .. } => "eat_item",
        ActionRequestPayload::Say { .. } => "say",
        ActionRequestPayload::Bubble { .. } => "bubble",
        ActionRequestPayload::Cancel { .. } => "cancel",
    }
}

fn execute_request(
    request: ActionRequest,
    companion_tile: &mut (i32, i32),
    state: &mut FakeState,
    mod_tick: u64,
) -> Envelope<Value> {
    let request_id = request.request_id.unwrap_or_else(|| "unknown".to_owned());
    match request.payload {
        ActionRequestPayload::Ping { actor_id } => ping_result(&request_id, &actor_id, mod_tick),
        ActionRequestPayload::MoveRelative {
            actor_id,
            direction,
            ticks,
        } => {
            let before = *companion_tile;
            let distance = (ticks / 5).max(1) as i32;
            apply_direction(companion_tile, direction, distance);
            move_result(
                &request_id,
                &actor_id,
                "move_relative",
                Some(direction.as_str()),
                ticks,
                before,
                *companion_tile,
                json!({"x": companion_tile.0, "y": companion_tile.1}),
            )
        }
        ActionRequestPayload::MoveTo { actor_id, x, y } => {
            let before = *companion_tile;
            *companion_tile = (x, y);
            move_result(
                &request_id,
                &actor_id,
                "move_to",
                None,
                0,
                before,
                *companion_tile,
                json!({"x": x, "y": y}),
            )
        }
        ActionRequestPayload::Follow { .. } => failure(
            &request_id,
            Some("follow"),
            Some(COMPANION_ID),
            "invalid_request",
            "follow requests must be started by the follow task handler".to_owned(),
        ),
        ActionRequestPayload::StartMode { actor_id, mode } => succeeded(
            &request_id,
            "start_mode",
            &actor_id,
            json!({"mode": mode, "state": "succeeded"}),
        ),
        ActionRequestPayload::FaceDirection {
            actor_id,
            direction,
        } => succeeded(
            &request_id,
            "face_direction",
            &actor_id,
            {
                state.facing_direction = direction.as_str().to_owned();
                json!({"direction": state.facing_direction})
            },
        ),
        ActionRequestPayload::UseTool {
            actor_id,
            tool,
            x,
            y,
        } => succeeded(
            &request_id,
            "use_tool",
            &actor_id,
            json!({"tool": tool.as_str(), "tile": {"x": x, "y": y}}),
        ),
        ActionRequestPayload::Interact { actor_id, x, y } => succeeded(
            &request_id,
            "interact",
            &actor_id,
            json!({"tile": {"x": x, "y": y}}),
        ),
        ActionRequestPayload::WarpTo {
            actor_id,
            location,
            x,
            y,
        } => succeeded(
            &request_id,
            "warp_to",
            &actor_id,
            {
                state.location = location.clone();
                *companion_tile = (x, y);
                json!({"location": state.location, "tile": {"x": x, "y": y}})
            },
        ),
        ActionRequestPayload::Observe { actor_id, radius } => succeeded(
            &request_id,
            "observe",
            &actor_id,
            json!({
                "observation": {
                    "location": state.location,
                    "center": {"x": companion_tile.0, "y": companion_tile.1},
                    "radius": radius,
                    "tiles": [],
                    "monsters": [],
                    "npcs": []
                }
            }),
        ),
        ActionRequestPayload::GetInventory { actor_id } => succeeded(
            &request_id,
            "get_inventory",
            &actor_id,
            json!({"inventory": []}),
        ),
        ActionRequestPayload::Attack { actor_id } => succeeded(
            &request_id,
            "attack",
            &actor_id,
            json!({"attacked": false}),
        ),
        ActionRequestPayload::CastFishingRod { actor_id } => succeeded(
            &request_id,
            "cast_fishing_rod",
            &actor_id,
            json!({"cast": true}),
        ),
        ActionRequestPayload::SetAutoCombat { actor_id, enabled } => succeeded(
            &request_id,
            "set_auto_combat",
            &actor_id,
            {
                state.auto_combat = enabled;
                json!({"enabled": state.auto_combat})
            },
        ),
        ActionRequestPayload::EatItem { actor_id, slot } => succeeded(
            &request_id,
            "eat_item",
            &actor_id,
            json!({"slot": slot, "ate": false}),
        ),
        ActionRequestPayload::Say { actor_id, text } => succeeded(
            &request_id,
            "say",
            &actor_id,
            json!({"text": text, "channel": "chat"}),
        ),
        ActionRequestPayload::Bubble {
            actor_id,
            text,
            duration_ms,
        } => succeeded(
            &request_id,
            "bubble",
            &actor_id,
            json!({"text": text, "channel": "bubble", "duration_ms": duration_ms}),
        ),
        ActionRequestPayload::Cancel {
            actor_id,
            target_request_id,
        } => succeeded(
            &request_id,
            "cancel",
            &actor_id,
            json!({"target_request_id": target_request_id, "cancelled": false}),
        ),
    }
}

fn apply_direction(tile: &mut (i32, i32), direction: Direction, distance: i32) {
    match direction {
        Direction::Up => tile.1 -= distance,
        Direction::Down => tile.1 += distance,
        Direction::Left => tile.0 -= distance,
        Direction::Right => tile.0 += distance,
    }
}

fn succeeded(request_id: &str, action: &str, actor_id: &str, data: Value) -> Envelope<Value> {
    Envelope {
        schema_version: SCHEMA_VERSION.to_owned(),
        message_type: "action.result".to_owned(),
        request_id: Some(request_id.to_owned()),
        created_at_ms: now_ms(),
        payload: json!({
            "status": "succeeded",
            "action": action,
            "actor_id": actor_id,
            "data": data,
        }),
    }
}

fn cancelled(
    request_id: &str,
    action: &str,
    actor_id: &str,
    code: &str,
    message: &str,
) -> Envelope<Value> {
    Envelope {
        schema_version: SCHEMA_VERSION.to_owned(),
        message_type: "action.result".to_owned(),
        request_id: Some(request_id.to_owned()),
        created_at_ms: now_ms(),
        payload: json!({
            "status": "cancelled",
            "action": action,
            "actor_id": actor_id,
            "error": {"code": code, "message": message},
        }),
    }
}

fn ping_result(request_id: &str, actor_id: &str, mod_tick: u64) -> Envelope<Value> {
    Envelope {
        schema_version: SCHEMA_VERSION.to_owned(),
        message_type: "action.result".to_owned(),
        request_id: Some(request_id.to_owned()),
        created_at_ms: now_ms(),
        payload: json!({
            "status": "succeeded",
            "action": "ping",
            "actor_id": actor_id,
            "mod_tick": mod_tick,
            "world_ready": true,
        }),
    }
}

fn move_result(
    request_id: &str,
    actor_id: &str,
    action: &str,
    direction: Option<&str>,
    ticks: u32,
    before: (i32, i32),
    after: (i32, i32),
    target_tile: Value,
) -> Envelope<Value> {
    Envelope {
        schema_version: SCHEMA_VERSION.to_owned(),
        message_type: "action.result".to_owned(),
        request_id: Some(request_id.to_owned()),
        created_at_ms: now_ms(),
        payload: json!({
            "status": "succeeded",
            "action": action,
            "actor_id": actor_id,
            "direction": direction,
            "ticks": ticks,
            "target_tile": target_tile,
            "before_tile": {"x": before.0, "y": before.1},
            "after_tile": {"x": after.0, "y": after.1},
            "moved": before != after,
            "world_ready": true,
        }),
    }
}

fn failure(
    request_id: &str,
    action: Option<&str>,
    actor_id: Option<&str>,
    code: &str,
    message: String,
) -> Envelope<Value> {
    let mut payload = json!({
        "status": "failed",
        "error": {"code": code, "message": message},
    });
    if let Some(action) = action {
        payload["action"] = json!(action);
    }
    if let Some(actor_id) = actor_id {
        payload["actor_id"] = json!(actor_id);
    }
    Envelope {
        schema_version: SCHEMA_VERSION.to_owned(),
        message_type: "action.result".to_owned(),
        request_id: Some(request_id.to_owned()),
        created_at_ms: now_ms(),
        payload,
    }
}

fn snapshot_envelope(
    snapshot_sequence: u64,
    latest_write_sequence: u64,
    player_tile: (i32, i32),
    companion_tile: (i32, i32),
    state: &FakeState,
    snapshot_index: usize,
) -> Envelope<Value> {
    let follow = state.follow.as_ref();
    Envelope {
        schema_version: SCHEMA_VERSION.to_owned(),
        message_type: "snapshot".to_owned(),
        request_id: None::<String>,
        created_at_ms: now_ms(),
        payload: json!({
            "latest_write_sequence": latest_write_sequence,
            "snapshot_sequence": snapshot_sequence,
            "snapshot_index": if snapshot_index == usize::MAX { -1 } else { snapshot_index as i64 },
            "mod_version": "0.2.0-fake",
            "game_tick": latest_write_sequence * 60,
            "world_ready": true,
            "game": {"year": 1, "season": "spring", "day": 1, "time": 900, "location": state.location, "weather": "sunny"},
            "player": {"name": "Player", "location": "Farm", "tile": {"x": player_tile.0, "y": player_tile.1}, "health": 100, "max_health": 100, "stamina": 270, "max_stamina": 270, "money": 500},
            "companion": {
                "id": "companion-1",
                "type": "ai_companion",
                "display_name": "Companion",
                "location": state.location,
                "tile": {"x": companion_tile.0, "y": companion_tile.1},
                "facing_direction": state.facing_direction,
                "health": 100,
                "max_health": 100,
                "stamina": 270,
                "max_stamina": 270,
                "inventory_count": 0,
                "inventory": [],
                "mode": if follow.is_some() { "follow" } else { "direct" },
                "status": if follow.is_some() { "following" } else { "idle" },
                "current_action": follow.map(|_| "follow"),
                "world_ready": true,
                "busy": follow.is_some(),
                "auto_combat": state.auto_combat,
                "follow": follow.map(|follow| json!({
                    "target_actor_id": follow.target_actor_id,
                    "distance": follow.distance,
                    "state": follow.state,
                    "target_location": "Farm",
                    "target_tile": {"x": player_tile.0, "y": player_tile.1},
                    "warp_count": follow.warp_count
                })),
                "capabilities": ["move_relative", "move_to", "face_direction", "use_tool", "interact", "warp_to", "observe", "get_inventory", "attack", "cast_fishing_rod", "set_auto_combat", "eat_item", "say", "bubble", "follow", "cancel"]
            }
        }),
    }
}

fn write_latest_snapshot(
    bridge: &Bridge,
    snapshot_sequence: u64,
    latest_write_sequence: u64,
    player_tile: (i32, i32),
    companion_tile: (i32, i32),
    state: &FakeState,
    snapshot_index: usize,
) -> Result<()> {
    let snapshot = snapshot_envelope(
        snapshot_sequence,
        latest_write_sequence,
        player_tile,
        companion_tile,
        state,
        snapshot_index,
    );
    replace_json(&bridge.root.join("snapshots/snapshot-latest.json"), &snapshot)
}

fn write_history_snapshot(
    bridge: &Bridge,
    snapshot_sequence: u64,
    latest_write_sequence: u64,
    player_tile: (i32, i32),
    companion_tile: (i32, i32),
    state: &FakeState,
    snapshot_history_limit: usize,
) -> Result<()> {
    let snapshot = snapshot_envelope(
        snapshot_sequence,
        latest_write_sequence,
        player_tile,
        companion_tile,
        state,
        usize::MAX,
    );
    bridge.write_snapshot(
        snapshot_sequence,
        latest_write_sequence,
        &snapshot,
        snapshot_history_limit,
    )
}
