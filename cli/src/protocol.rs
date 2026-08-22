use clap::ValueEnum;
use serde::{Deserialize, Serialize};

pub const SCHEMA_VERSION: &str = "0.1";
pub const COMPANION_ID: &str = "companion-1";

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Envelope<T> {
    pub schema_version: String,
    pub message_type: String,
    pub request_id: Option<String>,
    pub created_at_ms: u64,
    pub payload: T,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(tag = "action", rename_all = "snake_case")]
pub enum ActionRequestPayload {
    Ping { actor_id: String },
    MoveRelative {
        actor_id: String,
        direction: Direction,
        ticks: u32,
    },
}

#[derive(Debug, Clone, Copy, Serialize, Deserialize, ValueEnum)]
#[serde(rename_all = "lowercase")]
pub enum Direction {
    Up,
    Down,
    Left,
    Right,
}

impl Direction {
    pub fn as_str(self) -> &'static str {
        match self {
            Self::Up => "up",
            Self::Down => "down",
            Self::Left => "left",
            Self::Right => "right",
        }
    }
}

pub type ActionRequest = Envelope<ActionRequestPayload>;

pub fn action_request(request_id: String, payload: ActionRequestPayload) -> ActionRequest {
    Envelope {
        schema_version: SCHEMA_VERSION.to_owned(),
        message_type: "action.request".to_owned(),
        request_id: Some(request_id),
        created_at_ms: now_ms(),
        payload,
    }
}

pub fn now_ms() -> u64 {
    std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .expect("system clock is before Unix epoch")
        .as_millis() as u64
}
