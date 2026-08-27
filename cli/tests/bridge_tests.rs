use std::{
    fs,
    process::Command,
    sync::{Mutex, MutexGuard, OnceLock},
    thread,
    time::{Duration, Instant},
};

use serde_json::json;
use stardew_cli::{
    bridge::Bridge,
    protocol::{now_ms, ActionRequestPayload, Direction, Envelope, SCHEMA_VERSION, COMPANION_ID},
};
use uuid::Uuid;

fn temp_bridge() -> Bridge {
    Bridge::new(std::env::temp_dir().join(format!("stardew-agent-test-{}", Uuid::new_v4())))
}

fn fake_mod_lock() -> MutexGuard<'static, ()> {
    static LOCK: OnceLock<Mutex<()>> = OnceLock::new();
    LOCK.get_or_init(|| Mutex::new(())).lock().unwrap_or_else(|poisoned| poisoned.into_inner())
}

#[test]
fn fake_mod_completes_ping_and_writes_snapshot() {
    let _lock = fake_mod_lock();
    let bridge = temp_bridge();
    bridge.ensure_layout().unwrap();
    let child = Command::new(env!("CARGO_BIN_EXE_fake-mod"))
        .arg("--bridge-dir")
        .arg(&bridge.root)
        .arg("--snapshot-interval-ms")
        .arg("50")
        .arg("--snapshot-history-limit")
        .arg("3")
        .arg("--once")
        .spawn()
        .unwrap();

    let result = bridge
        .send_and_wait(
            ActionRequestPayload::Ping {
                actor_id: COMPANION_ID.to_owned(),
            },
            2_000,
        )
        .unwrap();
    let mut child = child;
    let status = child.wait().unwrap();

    assert!(status.success());
    assert_eq!(result["payload"]["status"], "succeeded");
    assert!(bridge.latest_snapshot().unwrap().is_some());
    fs::remove_dir_all(bridge.root).unwrap();
}

#[test]
fn fake_mod_applies_move_to_companion_tile() {
    let _lock = fake_mod_lock();
    let bridge = temp_bridge();
    bridge.ensure_layout().unwrap();
    let child = Command::new(env!("CARGO_BIN_EXE_fake-mod"))
        .arg("--bridge-dir")
        .arg(&bridge.root)
        .arg("--snapshot-interval-ms")
        .arg("50")
        .arg("--snapshot-history-limit")
        .arg("3")
        .arg("--once")
        .spawn()
        .unwrap();

    let result = bridge
        .send_and_wait(
            ActionRequestPayload::MoveRelative {
                actor_id: COMPANION_ID.to_owned(),
                direction: Direction::Right,
                ticks: 15,
            },
            2_000,
        )
        .unwrap();
    let mut child = child;
    assert!(child.wait().unwrap().success());

    assert_eq!(result["payload"]["action"], "move_relative");
    assert_eq!(result["payload"]["moved"], true);
    assert_ne!(result["payload"]["before_tile"], result["payload"]["after_tile"]);
    fs::remove_dir_all(bridge.root).unwrap();
}

#[test]
fn fake_mod_supports_companion_read_and_write_actions() {
    let _lock = fake_mod_lock();
    let actions = vec![
        (ActionRequestPayload::MoveTo {
            actor_id: COMPANION_ID.to_owned(),
            x: 72,
            y: 18,
        }, "move_to"),
        (ActionRequestPayload::FaceDirection {
            actor_id: COMPANION_ID.to_owned(),
            direction: Direction::Left,
        }, "face_direction"),
        (ActionRequestPayload::UseTool {
            actor_id: COMPANION_ID.to_owned(),
            tool: stardew_cli::protocol::ToolKind::Hoe,
            x: 72,
            y: 19,
        }, "use_tool"),
        (ActionRequestPayload::Interact {
            actor_id: COMPANION_ID.to_owned(),
            x: 72,
            y: 19,
        }, "interact"),
        (ActionRequestPayload::WarpTo {
            actor_id: COMPANION_ID.to_owned(),
            location: "Mine".to_owned(),
            x: 6,
            y: 6,
        }, "warp_to"),
        (ActionRequestPayload::StartMode {
            actor_id: COMPANION_ID.to_owned(),
            mode: "water_crops".to_owned(),
        }, "start_mode"),
        (ActionRequestPayload::Observe {
            actor_id: COMPANION_ID.to_owned(),
            radius: 8,
        }, "observe"),
        (ActionRequestPayload::GetInventory {
            actor_id: COMPANION_ID.to_owned(),
        }, "get_inventory"),
        (ActionRequestPayload::Attack {
            actor_id: COMPANION_ID.to_owned(),
        }, "attack"),
        (ActionRequestPayload::CastFishingRod {
            actor_id: COMPANION_ID.to_owned(),
        }, "cast_fishing_rod"),
        (ActionRequestPayload::SetAutoCombat {
            actor_id: COMPANION_ID.to_owned(),
            enabled: true,
        }, "set_auto_combat"),
        (ActionRequestPayload::EatItem {
            actor_id: COMPANION_ID.to_owned(),
            slot: None,
        }, "eat_item"),
        (ActionRequestPayload::Say {
            actor_id: COMPANION_ID.to_owned(),
            text: "I am ready to help.".to_owned(),
        }, "say"),
        (ActionRequestPayload::Bubble {
            actor_id: COMPANION_ID.to_owned(),
            text: "I am above the Companion.".to_owned(),
            duration_ms: 3_000,
        }, "bubble"),
        (ActionRequestPayload::Cancel {
            actor_id: COMPANION_ID.to_owned(),
            target_request_id: "missing-request".to_owned(),
        }, "cancel"),
    ];

    for (action, action_name) in actions {
        let bridge = temp_bridge();
        bridge.ensure_layout().unwrap();
        let child = Command::new(env!("CARGO_BIN_EXE_fake-mod"))
            .arg("--bridge-dir")
            .arg(&bridge.root)
            .arg("--snapshot-interval-ms")
            .arg("50")
            .arg("--once")
            .spawn()
            .unwrap();

        let result = bridge.send_and_wait(action, 2_000).unwrap();
        let mut child = child;
        assert!(child.wait().unwrap().success());
        assert_eq!(result["payload"]["action"], action_name);
        assert_eq!(result["payload"]["actor_id"], COMPANION_ID);
        fs::remove_dir_all(bridge.root).unwrap();
    }
}

#[test]
fn fake_mod_validates_actor_and_rotates_configured_history() {
    let _lock = fake_mod_lock();
    let bridge = temp_bridge();
    bridge.ensure_layout().unwrap();

    for _ in 0..5 {
        let child = Command::new(env!("CARGO_BIN_EXE_fake-mod"))
            .arg("--bridge-dir")
            .arg(&bridge.root)
            .arg("--latest-interval-ms")
            .arg("50")
            .arg("--snapshot-history-interval-ms")
            .arg("50")
            .arg("--snapshot-history-limit")
            .arg("3")
            .arg("--once")
            .spawn()
            .unwrap();

        let result = bridge
            .send_and_wait(
                ActionRequestPayload::Ping {
                    actor_id: COMPANION_ID.to_owned(),
                },
                2_000,
            )
            .unwrap();
        let mut child = child;
        assert!(child.wait().unwrap().success());
        assert_eq!(result["payload"]["status"], "succeeded");
    }

    let history_count = fs::read_dir(bridge.root.join("snapshots"))
        .unwrap()
        .filter_map(|entry| entry.ok().map(|item| item.path()))
        .filter(|path| {
            path.file_name().and_then(|value| value.to_str()) != Some("snapshot-latest.json")
                && path.extension().and_then(|value| value.to_str()) == Some("json")
        })
        .count();
    assert_eq!(history_count, 3);

    let child = Command::new(env!("CARGO_BIN_EXE_fake-mod"))
        .arg("--bridge-dir")
        .arg(&bridge.root)
        .arg("--snapshot-interval-ms")
        .arg("50")
        .arg("--snapshot-history-limit")
        .arg("3")
        .arg("--once")
        .spawn()
        .unwrap();
    let result = bridge
        .send_and_wait(
            ActionRequestPayload::Ping {
                actor_id: "not-a-companion".to_owned(),
            },
            2_000,
        )
        .unwrap();
    let mut child = child;
    assert!(child.wait().unwrap().success());
    assert_eq!(result["payload"]["status"], "failed");
    assert_eq!(result["payload"]["error"]["code"], "unsupported_actor");
    fs::remove_dir_all(bridge.root).unwrap();
}

#[test]
fn fake_mod_keeps_latest_and_history_periods_independent() {
    let _lock = fake_mod_lock();
    let bridge = temp_bridge();
    bridge.ensure_layout().unwrap();
    let mut child = Command::new(env!("CARGO_BIN_EXE_fake-mod"))
        .arg("--bridge-dir")
        .arg(&bridge.root)
        .arg("--latest-interval-ms")
        .arg("50")
        .arg("--snapshot-history-interval-ms")
        .arg("1000")
        .arg("--snapshot-history-limit")
        .arg("3")
        .spawn()
        .unwrap();

    let deadline = Instant::now() + Duration::from_secs(2);
    let mut observed_independent_write = false;
    while Instant::now() < deadline {
        if let Some(snapshot) = bridge.latest_snapshot().unwrap() {
            let latest_sequence = snapshot
                .payload
                .get("latest_write_sequence")
                .and_then(serde_json::Value::as_u64)
                .unwrap_or(0);
            let history_sequence = snapshot
                .payload
                .get("snapshot_sequence")
                .and_then(serde_json::Value::as_u64)
                .unwrap_or(0);
            if latest_sequence > history_sequence {
                observed_independent_write = true;
                break;
            }
        }
        thread::sleep(Duration::from_millis(25));
    }

    child.kill().unwrap();
    let _ = child.wait();
    assert!(observed_independent_write);
    fs::remove_dir_all(bridge.root).unwrap();
}

#[test]
fn snapshot_history_rotates_and_latest_is_full_state() {
    let bridge = temp_bridge();
    bridge.ensure_layout().unwrap();

    for sequence in 1..=12 {
        let snapshot = Envelope {
            schema_version: SCHEMA_VERSION.to_owned(),
            message_type: "snapshot".to_owned(),
            request_id: None::<String>,
            created_at_ms: now_ms(),
            payload: json!({
                "latest_write_sequence": sequence,
                "snapshot_sequence": sequence,
                "snapshot_index": -1,
                "companion": {"id": COMPANION_ID, "tile": {"x": sequence, "y": 15}}
            }),
        };
        bridge
            .write_snapshot(sequence, sequence, &snapshot, 10)
            .unwrap();
    }

    let history_count = fs::read_dir(bridge.root.join("snapshots"))
        .unwrap()
        .filter_map(|entry| entry.ok().map(|item| item.path()))
        .filter(|path| {
            path.file_name().and_then(|value| value.to_str()) != Some("snapshot-latest.json")
                && path.extension().and_then(|value| value.to_str()) == Some("json")
        })
        .count();
    assert_eq!(history_count, 10);
    let latest_raw = fs::read_to_string(bridge.root.join("snapshots/snapshot-latest.json")).unwrap();
    let latest: serde_json::Value = serde_json::from_str(&latest_raw).unwrap();
    assert_eq!(latest["message_type"], "snapshot");
    assert_eq!(latest["payload"]["latest_write_sequence"], 12);
    assert_eq!(latest["payload"]["snapshot_index"], 1);
    assert_eq!(
        bridge
            .latest_snapshot()
            .unwrap()
            .unwrap()
            .payload["snapshot_sequence"],
        12
    );
    fs::remove_dir_all(bridge.root).unwrap();
}

#[test]
fn cli_reads_state_and_bridge_diagnostics_as_json() {
    let bridge = temp_bridge();
    bridge.ensure_layout().unwrap();
    let snapshot = Envelope {
        schema_version: SCHEMA_VERSION.to_owned(),
        message_type: "snapshot".to_owned(),
        request_id: None::<String>,
        created_at_ms: now_ms(),
        payload: json!({
            "latest_write_sequence": 3,
            "snapshot_sequence": 2,
            "snapshot_index": 1,
            "world_ready": true,
            "game": {"location": "Farm"},
            "player": {"name": "Player"},
            "companion": {"id": COMPANION_ID, "tile": {"x": 65, "y": 15}}
        }),
    };
    bridge.write_snapshot(2, 3, &snapshot, 3).unwrap();

    for command in ["status", "companion", "doctor", "snapshot", "cleanup"] {
        let mut cli = Command::new(env!("CARGO_BIN_EXE_stardew-cli"));
        cli.arg("--bridge-dir").arg(&bridge.root);
        match command {
            "snapshot" => {
                cli.arg("snapshot").arg("list");
            }
            "cleanup" => {
                cli.arg("cleanup").arg("--dry-run");
            }
            _ => {
                cli.arg(command);
            }
        }
        let output = cli.output().unwrap();
        assert!(output.status.success(), "{command}: {:?}", output);
        let value: serde_json::Value = serde_json::from_slice(&output.stdout).unwrap();
        assert!(!value.is_null());
    }

    fs::remove_dir_all(bridge.root).unwrap();
}

#[test]
fn cli_defaults_to_bridge_next_to_executable() {
    let root = std::env::temp_dir().join(format!("stardew-agent-cli-default-{}", Uuid::new_v4()));
    let bridge = Bridge::new(root.join("bridge"));
    bridge.ensure_layout().unwrap();
    let snapshot = Envelope {
        schema_version: SCHEMA_VERSION.to_owned(),
        message_type: "snapshot".to_owned(),
        request_id: None::<String>,
        created_at_ms: now_ms(),
        payload: json!({
            "latest_write_sequence": 1,
            "snapshot_sequence": 1,
            "snapshot_index": 0,
            "world_ready": true,
            "game": {"location": "Farm"},
            "player": {"name": "Player"},
            "companion": {"id": COMPANION_ID}
        }),
    };
    bridge.write_snapshot(1, 1, &snapshot, 1).unwrap();

    let source_cli = std::path::Path::new(env!("CARGO_BIN_EXE_stardew-cli"));
    let cli_path = root.join(source_cli.file_name().unwrap());
    fs::copy(source_cli, &cli_path).unwrap();
    let output = Command::new(&cli_path)
        .current_dir(std::env::temp_dir())
        .arg("status")
        .output()
        .unwrap();
    assert!(output.status.success(), "CLI stderr: {}", String::from_utf8_lossy(&output.stderr));
    let value: serde_json::Value = serde_json::from_slice(&output.stdout).unwrap();
    assert_eq!(value["payload"]["game"]["location"], "Farm");

    fs::remove_dir_all(root).unwrap();
}

#[test]
fn cli_reads_information_commands_synchronously() {
    let _lock = fake_mod_lock();
    for (arguments, action_name) in [
        (vec!["ping"], "ping"),
        (vec!["observe", "--radius", "8"], "observe"),
        (vec!["inventory"], "get_inventory"),
    ] {
        let bridge = temp_bridge();
        bridge.ensure_layout().unwrap();
        let child = Command::new(env!("CARGO_BIN_EXE_fake-mod"))
            .arg("--bridge-dir")
            .arg(&bridge.root)
            .arg("--latest-interval-ms")
            .arg("50")
            .arg("--once")
            .spawn()
            .unwrap();

        let mut cli = Command::new(env!("CARGO_BIN_EXE_stardew-cli"));
        cli.arg("--bridge-dir").arg(&bridge.root);
        for argument in arguments {
            cli.arg(argument);
        }
        let output = cli.output().unwrap();
        let mut child = child;
        assert!(output.status.success(), "CLI stderr: {}", String::from_utf8_lossy(&output.stderr));
        assert!(child.wait().unwrap().success());
        let result: serde_json::Value = serde_json::from_slice(&output.stdout).unwrap();
        assert_eq!(result["payload"]["action"], action_name);
        assert_eq!(result["payload"]["status"], "succeeded");
        assert!(result.get("request_id").is_some());
        fs::remove_dir_all(bridge.root).unwrap();
    }
}

#[test]
fn cli_writes_game_actions_asynchronous_receipts() {
    let _lock = fake_mod_lock();
    for (arguments, action_name) in [
        (vec!["move", "right", "--ticks", "10"], "move_relative"),
        (vec!["say", "hello from the CLI"], "say"),
        (vec!["bubble", "hello above the Companion"], "bubble"),
    ] {
        let bridge = temp_bridge();
        bridge.ensure_layout().unwrap();
        let child = Command::new(env!("CARGO_BIN_EXE_fake-mod"))
            .arg("--bridge-dir")
            .arg(&bridge.root)
            .arg("--latest-interval-ms")
            .arg("50")
            .arg("--once")
            .spawn()
            .unwrap();

        let mut cli = Command::new(env!("CARGO_BIN_EXE_stardew-cli"));
        cli.arg("--bridge-dir").arg(&bridge.root);
        for argument in arguments {
            cli.arg(argument);
        }
        let output = cli.output().unwrap();
        let mut child = child;
        assert!(output.status.success(), "CLI stderr: {}", String::from_utf8_lossy(&output.stderr));
        assert!(child.wait().unwrap().success());
        let receipt: serde_json::Value = serde_json::from_slice(&output.stdout).unwrap();
        assert_eq!(receipt["status"], "accepted");
        assert_eq!(receipt["action"], action_name);
        let request_id = receipt["request_id"].as_str().unwrap();
        let wait_output = Command::new(env!("CARGO_BIN_EXE_stardew-cli"))
            .arg("--bridge-dir")
            .arg(&bridge.root)
            .arg("wait")
            .arg(request_id)
            .output()
            .unwrap();
        assert!(
            wait_output.status.success(),
            "wait stderr: {}",
            String::from_utf8_lossy(&wait_output.stderr)
        );
        let result: serde_json::Value = serde_json::from_slice(&wait_output.stdout).unwrap();
        assert_eq!(result["payload"]["action"], action_name);
        fs::remove_dir_all(bridge.root).unwrap();
    }
}

#[test]
fn fake_mod_prioritizes_cancel_and_cancels_pending_action() {
    let _lock = fake_mod_lock();
    let bridge = temp_bridge();
    bridge.ensure_layout().unwrap();
    let target_request_id = bridge
        .submit(ActionRequestPayload::MoveTo {
            actor_id: COMPANION_ID.to_owned(),
            x: 120,
            y: 80,
        })
        .unwrap();
    let cancel_request_id = bridge
        .submit(ActionRequestPayload::Cancel {
            actor_id: COMPANION_ID.to_owned(),
            target_request_id: target_request_id.clone(),
        })
        .unwrap();

    let child = Command::new(env!("CARGO_BIN_EXE_fake-mod"))
        .arg("--bridge-dir")
        .arg(&bridge.root)
        .arg("--once")
        .spawn()
        .unwrap();
    let mut child = child;
    assert!(child.wait().unwrap().success());

    let target_result = bridge.wait(&target_request_id, 2_000).unwrap();
    let cancel_result = bridge.wait(&cancel_request_id, 2_000).unwrap();
    assert_eq!(target_result["payload"]["status"], "cancelled");
    assert_eq!(cancel_result["payload"]["status"], "succeeded");
    assert_eq!(cancel_result["payload"]["data"]["cancelled"], true);
    fs::remove_dir_all(bridge.root).unwrap();
}

#[test]
fn cli_follow_stays_pending_until_cancelled() {
    let _lock = fake_mod_lock();
    let bridge = temp_bridge();
    bridge.ensure_layout().unwrap();
    let mut child = Command::new(env!("CARGO_BIN_EXE_fake-mod"))
        .arg("--bridge-dir")
        .arg(&bridge.root)
        .arg("--latest-interval-ms")
        .arg("50")
        .spawn()
        .unwrap();

    let output = Command::new(env!("CARGO_BIN_EXE_stardew-cli"))
        .arg("--bridge-dir")
        .arg(&bridge.root)
        .arg("follow")
        .arg("--distance")
        .arg("2")
        .output()
        .unwrap();
    assert!(output.status.success(), "CLI stderr: {}", String::from_utf8_lossy(&output.stderr));
    let receipt: serde_json::Value = serde_json::from_slice(&output.stdout).unwrap();
    assert_eq!(receipt["status"], "accepted");
    assert_eq!(receipt["action"], "follow");
    let follow_request_id = receipt["request_id"].as_str().unwrap().to_owned();

    let deadline = Instant::now() + Duration::from_secs(2);
    let mut following = false;
    while Instant::now() < deadline {
        if let Some(snapshot) = bridge.latest_snapshot().unwrap() {
            if snapshot.payload["companion"]["current_action"] == "follow" {
                following = true;
                break;
            }
        }
        thread::sleep(Duration::from_millis(25));
    }
    assert!(following, "fake mod never exposed the active follow state");
    assert!(bridge.read_result(&follow_request_id).unwrap().is_none());

    let cancel_request_id = bridge
        .submit(ActionRequestPayload::Cancel {
            actor_id: COMPANION_ID.to_owned(),
            target_request_id: follow_request_id.clone(),
        })
        .unwrap();
    let cancel_result = bridge.wait(&cancel_request_id, 2_000).unwrap();
    let follow_result = bridge.wait(&follow_request_id, 2_000).unwrap();
    assert_eq!(cancel_result["payload"]["status"], "succeeded");
    assert_eq!(cancel_result["payload"]["data"]["target_action"], "follow");
    assert_eq!(follow_result["payload"]["status"], "cancelled");
    assert_eq!(follow_result["payload"]["action"], "follow");

    child.kill().unwrap();
    let _ = child.wait();
    fs::remove_dir_all(bridge.root).unwrap();
}
