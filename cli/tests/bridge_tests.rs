use std::{fs, process::Command};

use serde_json::json;
use stardew_cli::{
    bridge::Bridge,
    protocol::{now_ms, ActionRequestPayload, Direction, Envelope, SCHEMA_VERSION, COMPANION_ID},
};
use uuid::Uuid;

fn temp_bridge() -> Bridge {
    Bridge::new(std::env::temp_dir().join(format!("stardew-agent-test-{}", Uuid::new_v4())))
}

#[test]
fn fake_mod_completes_ping_and_writes_snapshot() {
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
