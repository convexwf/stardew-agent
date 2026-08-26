use std::{path::PathBuf, process::ExitCode, thread, time::Duration};

use anyhow::{bail, Context, Result};
use clap::{Parser, Subcommand};
use serde_json::{json, Value};

use stardew_cli::{
    bridge::Bridge,
    protocol::{ActionRequestPayload, Direction, ToolKind, COMPANION_ID},
};

#[derive(Debug, Parser)]
#[command(name = "stardew-cli", version, about = "Stardew Agent CLI bridge")]
struct Cli {
    #[arg(long, global = true, env = "STARDEW_BRIDGE_DIR")]
    bridge_dir: Option<PathBuf>,

    #[command(subcommand)]
    command: Command,
}

#[derive(Debug, Subcommand)]
enum Command {
    /// Print the complete latest state projection.
    #[command(alias = "state")]
    Status,
    /// Print only the world section of the latest state.
    World,
    /// Print only the host farmer section of the latest state.
    Player,
    /// Print the selected Companion section of the latest state.
    #[command(alias = "get-companion-state")]
    Companion {
        #[arg(long, default_value = "companion-1")]
        actor_id: String,
    },
    /// Request a live Companion inventory from the Mod.
    #[command(alias = "get-inventory")]
    Inventory {
        #[arg(long, default_value = "companion-1")]
        actor_id: String,
    },
    /// Request a live surroundings scan from the Mod.
    #[command(alias = "get-surroundings")]
    Observe {
        #[arg(long, default_value = "companion-1")]
        actor_id: String,
        #[arg(long, default_value_t = 8, value_parser = clap::value_parser!(u32).range(1..=16))]
        radius: u32,
    },
    /// Inspect snapshot slots.
    Snapshot {
        #[command(subcommand)]
        command: SnapshotCommand,
    },
    /// Inspect an action request by ID.
    Request {
        #[command(subcommand)]
        command: RequestCommand,
    },
    /// Inspect an action result by ID.
    Result {
        #[command(subcommand)]
        command: ResultCommand,
    },
    /// Wait for an already submitted action request to reach a terminal result.
    Wait {
        request_id: String,
        #[arg(long, default_value_t = 30_000)]
        timeout_ms: u64,
    },
    /// Check Bridge directories, latest state and temporary files.
    Doctor,
    /// Remove old result, archive and error files.
    Cleanup {
        #[arg(long, default_value_t = 86_400)]
        older_than_seconds: u64,
        #[arg(long)]
        dry_run: bool,
    },
    /// Test the CLI -> Mod -> CLI request path.
    Ping,
    /// Move the Companion by a bounded relative path.
    Move {
        #[arg(value_enum)]
        direction: Direction,
        #[arg(long, value_parser = clap::value_parser!(u32).range(1..=30))]
        ticks: u32,
    },
    /// Move the Companion to a target tile using game pathfinding.
    #[command(alias = "move_to")]
    MoveTo {
        #[arg(long)]
        x: i32,
        #[arg(long)]
        y: i32,
    },
    /// Change the Companion facing direction.
    #[command(alias = "face_direction")]
    Face {
        #[arg(value_enum)]
        direction: Direction,
    },
    /// Use a tool at a target tile.
    #[command(alias = "use_tool")]
    UseTool {
        #[arg(value_enum)]
        tool: ToolKind,
        #[arg(long)]
        x: i32,
        #[arg(long)]
        y: i32,
    },
    /// Interact with a tile, crop, chest, machine or ladder.
    Interact {
        #[arg(long)]
        x: i32,
        #[arg(long)]
        y: i32,
    },
    /// Explicitly warp the Companion to a location and tile.
    #[command(alias = "warp-companion")]
    Warp {
        #[arg(long)]
        location: String,
        #[arg(long)]
        x: i32,
        #[arg(long)]
        y: i32,
    },
    /// Attack the nearest monster in range.
    Attack,
    /// Cast the Companion fishing rod.
    #[command(alias = "cast_fishing_rod")]
    CastFishingRod,
    /// Toggle real-time Companion auto-combat.
    #[command(alias = "set_auto_combat")]
    SetAutoCombat {
        #[arg(long)]
        enabled: bool,
    },
    /// Eat a food item from the Companion inventory.
    #[command(alias = "eat_item")]
    EatItem {
        #[arg(long)]
        slot: Option<usize>,
    },
    /// Show a message from the Companion in the game's chat window.
    #[command(alias = "chat")]
    Say {
        text: String,
        #[arg(long, default_value = "companion-1")]
        actor_id: String,
    },
    /// Show a temporary speech bubble above the Companion.
    Bubble {
        text: String,
        #[arg(long, default_value = "companion-1")]
        actor_id: String,
        #[arg(long, default_value_t = 3_000, value_parser = clap::value_parser!(u64).range(250..=30_000))]
        duration_ms: u64,
    },
    /// Cancel any not-yet-completed action request.
    Cancel { target_request_id: String },
    /// Print state whenever latest_write_sequence changes.
    Watch {
        #[arg(long, default_value_t = 1_000)]
        interval_ms: u64,
    },
}

#[derive(Debug, Subcommand)]
enum SnapshotCommand {
    List,
    Read { index: usize },
}

#[derive(Debug, Subcommand)]
enum RequestCommand {
    Show { request_id: String },
}

#[derive(Debug, Subcommand)]
enum ResultCommand {
    Show { request_id: String },
}

fn main() -> ExitCode {
    match run() {
        Ok(()) => ExitCode::SUCCESS,
        Err(error) => {
            eprintln!("stardew-cli: {error:#}");
            ExitCode::from(1)
        }
    }
}

fn run() -> Result<()> {
    let cli = Cli::parse();
    let bridge_dir = cli.bridge_dir.unwrap_or(default_bridge_dir()?);
    let bridge = Bridge::new(bridge_dir);
    bridge.ensure_layout()?;

    match cli.command {
        Command::Status => print_latest(&bridge),
        Command::World => print_section(&bridge, "game"),
        Command::Player => print_section(&bridge, "player"),
        Command::Companion { actor_id } => print_companion(&bridge, &actor_id),
        Command::Inventory { actor_id } => submit_and_print(
            &bridge,
            ActionRequestPayload::GetInventory { actor_id },
        ),
        Command::Observe { actor_id, radius } => submit_and_print(
            &bridge,
            ActionRequestPayload::Observe { actor_id, radius },
        ),
        Command::Snapshot { command } => match command {
            SnapshotCommand::List => {
                let snapshots = bridge.snapshot_slots()?;
                let summaries = snapshots
                    .iter()
                    .map(|snapshot| {
                        json!({
                            "snapshot_sequence": snapshot.payload.get("snapshot_sequence"),
                            "snapshot_index": snapshot.payload.get("snapshot_index"),
                            "latest_write_sequence": snapshot.payload.get("latest_write_sequence"),
                            "created_at_ms": snapshot.created_at_ms,
                        })
                    })
                    .collect::<Vec<_>>();
                print_json(&Value::Array(summaries))
            }
            SnapshotCommand::Read { index } => {
                let snapshot = bridge
                    .snapshot_slot(index)?
                    .with_context(|| format!("snapshot slot {index} not found"))?;
                print_json(&serde_json::to_value(snapshot)?)
            }
        },
        Command::Request { command } => match command {
            RequestCommand::Show { request_id } => print_optional(bridge.read_request(&request_id)?, "request"),
        },
        Command::Result { command } => match command {
            ResultCommand::Show { request_id } => print_optional(bridge.read_result(&request_id)?, "result"),
        },
        Command::Wait {
            request_id,
            timeout_ms,
        } => wait_and_print(&bridge, &request_id, timeout_ms),
        Command::Doctor => print_json(&bridge.doctor()?),
        Command::Cleanup {
            older_than_seconds,
            dry_run,
        } => print_json(&bridge.cleanup(older_than_seconds, dry_run)?),
        Command::Ping => submit_and_print(
            &bridge,
            ActionRequestPayload::Ping {
                actor_id: COMPANION_ID.to_owned(),
            },
        ),
        Command::Move { direction, ticks } => submit_and_print(
            &bridge,
            ActionRequestPayload::MoveRelative {
                actor_id: COMPANION_ID.to_owned(),
                direction,
                ticks,
            },
        ),
        Command::MoveTo { x, y } => submit_and_print(
            &bridge,
            ActionRequestPayload::MoveTo {
                actor_id: COMPANION_ID.to_owned(),
                x,
                y,
            },
        ),
        Command::Face { direction } => submit_and_print(
            &bridge,
            ActionRequestPayload::FaceDirection {
                actor_id: COMPANION_ID.to_owned(),
                direction,
            },
        ),
        Command::UseTool { tool, x, y } => submit_and_print(
            &bridge,
            ActionRequestPayload::UseTool {
                actor_id: COMPANION_ID.to_owned(),
                tool,
                x,
                y,
            },
        ),
        Command::Interact { x, y } => submit_and_print(
            &bridge,
            ActionRequestPayload::Interact {
                actor_id: COMPANION_ID.to_owned(),
                x,
                y,
            },
        ),
        Command::Warp { location, x, y } => submit_and_print(
            &bridge,
            ActionRequestPayload::WarpTo {
                actor_id: COMPANION_ID.to_owned(),
                location,
                x,
                y,
            },
        ),
        Command::Attack => submit_and_print(
            &bridge,
            ActionRequestPayload::Attack {
                actor_id: COMPANION_ID.to_owned(),
            },
        ),
        Command::CastFishingRod => submit_and_print(
            &bridge,
            ActionRequestPayload::CastFishingRod {
                actor_id: COMPANION_ID.to_owned(),
            },
        ),
        Command::SetAutoCombat { enabled } => submit_and_print(
            &bridge,
            ActionRequestPayload::SetAutoCombat {
                actor_id: COMPANION_ID.to_owned(),
                enabled,
            },
        ),
        Command::EatItem { slot } => submit_and_print(
            &bridge,
            ActionRequestPayload::EatItem {
                actor_id: COMPANION_ID.to_owned(),
                slot,
            },
        ),
        Command::Say { text, actor_id } => submit_and_print(
            &bridge,
            ActionRequestPayload::Say { actor_id, text },
        ),
        Command::Bubble { text, actor_id, duration_ms } => submit_and_print(
            &bridge,
            ActionRequestPayload::Bubble {
                actor_id,
                text,
                duration_ms,
            },
        ),
        Command::Cancel { target_request_id } => submit_and_print(
            &bridge,
            ActionRequestPayload::Cancel {
                actor_id: COMPANION_ID.to_owned(),
                target_request_id,
            },
        ),
        Command::Watch { interval_ms } => watch(&bridge, interval_ms),
    }
}

fn default_bridge_dir() -> Result<PathBuf> {
    let executable = std::env::current_exe().context("resolve CLI executable path")?;
    let executable_dir = executable
        .parent()
        .context("resolve CLI executable directory")?;
    Ok(executable_dir.join("bridge"))
}

fn print_latest(bridge: &Bridge) -> Result<()> {
    let snapshot = bridge.latest_snapshot()?.context("no snapshot available")?;
    print_json(&serde_json::to_value(snapshot)?)
}

fn print_section(bridge: &Bridge, section: &str) -> Result<()> {
    let snapshot = bridge.latest_snapshot()?.context("no snapshot available")?;
    let value = snapshot
        .payload
        .get(section)
        .cloned()
        .with_context(|| format!("snapshot does not contain {section}"))?;
    print_json(&value)
}

fn print_companion(bridge: &Bridge, actor_id: &str) -> Result<()> {
    let snapshot = bridge.latest_snapshot()?.context("no snapshot available")?;
    let companion = snapshot
        .payload
        .get("companion")
        .filter(|value| value.get("id").and_then(Value::as_str) == Some(actor_id))
        .cloned()
        .with_context(|| format!("snapshot does not contain actor {actor_id}"))?;
    print_json(&companion)
}

fn print_optional(value: Option<Value>, kind: &str) -> Result<()> {
    let value = value.with_context(|| format!("no {kind} found"))?;
    print_json(&value)
}

fn submit_and_print(bridge: &Bridge, payload: ActionRequestPayload) -> Result<()> {
    let receipt = bridge.submit_receipt(payload)?;
    print_json(&receipt)
}

fn wait_and_print(bridge: &Bridge, request_id: &str, timeout_ms: u64) -> Result<()> {
    let result = bridge.wait(request_id, timeout_ms)?;
    print_result(&result)
}

fn print_result(result: &Value) -> Result<()> {
    print_json(result)?;
    let status = result
        .get("payload")
        .and_then(|payload| payload.get("status"))
        .and_then(Value::as_str)
        .unwrap_or("unknown");
    if matches!(status, "failed" | "blocked" | "cancelled" | "expired") {
        bail!("Mod returned status {status}");
    }
    Ok(())
}

fn print_json(value: &Value) -> Result<()> {
    println!("{}", serde_json::to_string_pretty(value)?);
    Ok(())
}

fn watch(bridge: &Bridge, interval_ms: u64) -> Result<()> {
    let interval = Duration::from_millis(interval_ms.max(50));
    let mut last_sequence = None;
    loop {
        if let Some(snapshot) = bridge.latest_snapshot()? {
            let sequence = snapshot
                .payload
                .get("latest_write_sequence")
                .and_then(Value::as_u64);
            if sequence != last_sequence {
                print_json(&serde_json::to_value(snapshot)?)?;
                last_sequence = sequence;
            }
        }
        thread::sleep(interval);
    }
}
