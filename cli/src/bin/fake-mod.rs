use std::{path::PathBuf, process::ExitCode};

use anyhow::Result;
use clap::Parser;

#[derive(Debug, Parser)]
#[command(name = "fake-mod", about = "Fake SMAPI Mod for Mac-side bridge tests")]
struct Args {
    #[arg(long)]
    bridge_dir: PathBuf,
    #[arg(long, default_value_t = 1_000)]
    snapshot_interval_ms: u64,
    #[arg(long)]
    once: bool,
}

fn main() -> ExitCode {
    match run() {
        Ok(()) => ExitCode::SUCCESS,
        Err(error) => {
            eprintln!("fake-mod: {error:#}");
            ExitCode::from(1)
        }
    }
}

fn run() -> Result<()> {
    let args = Args::parse();
    stardew_cli::fake::run(args.bridge_dir, args.snapshot_interval_ms, args.once)
}
