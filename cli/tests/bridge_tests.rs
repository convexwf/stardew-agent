use std::{fs, process::Command};

use stardew_cli::{
    bridge::Bridge,
    protocol::{ActionRequestPayload, Direction},
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
        .send_and_wait(ActionRequestPayload::Ping, 2_000)
        .unwrap();
    let mut child = child;
    let status = child.wait().unwrap();

    assert!(status.success());
    assert_eq!(result["payload"]["status"], "succeeded");
    assert!(bridge.latest_snapshot().unwrap().is_some());
    fs::remove_dir_all(bridge.root).unwrap();
}

#[test]
fn fake_mod_applies_move_to_player_tile() {
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
