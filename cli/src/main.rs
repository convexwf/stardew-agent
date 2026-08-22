use std::{path::PathBuf, process::ExitCode, thread, time::Duration};

use anyhow::{bail, Context, Result};
use clap::{Parser, Subcommand};

use stardew_cli::{
    bridge::Bridge,
    protocol::{ActionRequestPayload, Direction, COMPANION_ID},
};

#[derive(Debug, Parser)]
#[command(name = "stardew-cli", version, about = "Stardew Agent CLI bridge demo")]
struct Cli {
    #[arg(long, global = true, env = "STARDEW_BRIDGE_DIR")]
    bridge_dir: Option<PathBuf>,

    #[command(subcommand)]
    command: Command,
}

#[derive(Debug, Subcommand)]
enum Command {
    Status,
    Ping {
        #[arg(long, default_value_t = 5_000)]
        timeout_ms: u64,
    },
    Move {
        #[arg(value_enum)]
        direction: Direction,
        #[arg(long, value_parser = clap::value_parser!(u32).range(1..=30))]
        ticks: u32,
        #[arg(long, default_value_t = 5_000)]
        timeout_ms: u64,
    },
    Watch {
        #[arg(long, default_value_t = 1_000)]
        interval_ms: u64,
    },
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
    let bridge_dir = cli
        .bridge_dir
        .context("missing --bridge-dir or STARDEW_BRIDGE_DIR")?;
    let bridge = Bridge::new(bridge_dir);
    bridge.ensure_layout()?;

    match cli.command {
        Command::Status => {
            let snapshot = bridge.latest_snapshot()?.context("no snapshot available")?;
            println!("{}", serde_json::to_string_pretty(&snapshot)?);
        }
        Command::Ping { timeout_ms } => {
            let result = bridge.send_and_wait(
                ActionRequestPayload::Ping {
                    actor_id: COMPANION_ID.to_owned(),
                },
                timeout_ms,
            )?;
            print_result(&result)?;
        }
        Command::Move {
            direction,
            ticks,
            timeout_ms,
        } => {
            let result = bridge.send_and_wait(
                ActionRequestPayload::MoveRelative {
                    actor_id: COMPANION_ID.to_owned(),
                    direction,
                    ticks,
                },
                timeout_ms,
            )?;
            print_result(&result)?;
        }
        Command::Watch { interval_ms } => {
            let interval = Duration::from_millis(interval_ms.max(50));
            let mut last_sequence = None;
            loop {
                if let Some(snapshot) = bridge.latest_snapshot()? {
                    let sequence = snapshot
                        .payload
                        .get("latest_write_sequence")
                        .and_then(serde_json::Value::as_u64);
                    if sequence != last_sequence {
                        println!("{}", serde_json::to_string_pretty(&snapshot)?);
                        last_sequence = sequence;
                    }
                }
                thread::sleep(interval);
            }
        }
    }

    Ok(())
}

fn print_result(result: &serde_json::Value) -> Result<()> {
    println!("{}", serde_json::to_string_pretty(result)?);
    let status = result
        .get("payload")
        .and_then(|payload| payload.get("status"))
        .and_then(serde_json::Value::as_str)
        .unwrap_or("unknown");
    if matches!(status, "failed" | "blocked") {
        bail!("Mod returned status {status}");
    }
    Ok(())
}
