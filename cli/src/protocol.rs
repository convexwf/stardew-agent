use clap::ValueEnum;
use serde::{Deserialize, Serialize};

pub const SCHEMA_VERSION: &str = "0.2";
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
    Ping {
        actor_id: String,
    },
    MoveRelative {
        actor_id: String,
        direction: Direction,
        ticks: u32,
    },
    MoveTo {
        actor_id: String,
        x: i32,
        y: i32,
    },
    Follow {
        actor_id: String,
        target_actor_id: String,
        distance: u32,
    },
    FaceDirection {
        actor_id: String,
        direction: Direction,
    },
    UseTool {
        actor_id: String,
        tool: ToolKind,
        x: i32,
        y: i32,
    },
    Interact {
        actor_id: String,
        x: i32,
        y: i32,
    },
    WarpTo {
        actor_id: String,
        location: String,
        x: i32,
        y: i32,
    },
    Observe {
        actor_id: String,
        radius: u32,
    },
    GetInventory {
        actor_id: String,
    },
    Attack {
        actor_id: String,
    },
    CastFishingRod {
        actor_id: String,
    },
    SetAutoCombat {
        actor_id: String,
        enabled: bool,
    },
    EatItem {
        actor_id: String,
        slot: Option<usize>,
    },
    Say {
        actor_id: String,
        text: String,
    },
    Bubble {
        actor_id: String,
        text: String,
        duration_ms: u64,
    },
    Cancel {
        actor_id: String,
        target_request_id: String,
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

#[derive(Debug, Clone, Copy, Serialize, Deserialize, ValueEnum)]
#[serde(rename_all = "snake_case")]
pub enum ToolKind {
    Pickaxe,
    Axe,
    Hoe,
    WateringCan,
    Sword,
}

impl ToolKind {
    pub fn as_str(self) -> &'static str {
        match self {
            Self::Pickaxe => "pickaxe",
            Self::Axe => "axe",
            Self::Hoe => "hoe",
            Self::WateringCan => "watering_can",
            Self::Sword => "sword",
        }
    }
}

pub type ActionRequest = Envelope<ActionRequestPayload>;

impl ActionRequestPayload {
    pub fn action_name(&self) -> &'static str {
        match self {
            Self::Ping { .. } => "ping",
            Self::MoveRelative { .. } => "move_relative",
            Self::MoveTo { .. } => "move_to",
            Self::Follow { .. } => "follow",
            Self::FaceDirection { .. } => "face_direction",
            Self::UseTool { .. } => "use_tool",
            Self::Interact { .. } => "interact",
            Self::WarpTo { .. } => "warp_to",
            Self::Observe { .. } => "observe",
            Self::GetInventory { .. } => "get_inventory",
            Self::Attack { .. } => "attack",
            Self::CastFishingRod { .. } => "cast_fishing_rod",
            Self::SetAutoCombat { .. } => "set_auto_combat",
            Self::EatItem { .. } => "eat_item",
            Self::Say { .. } => "say",
            Self::Bubble { .. } => "bubble",
            Self::Cancel { .. } => "cancel",
        }
    }
}

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
