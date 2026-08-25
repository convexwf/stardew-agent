using System.Text.Json;
using System.Text.Json.Serialization;

namespace StardewAgentMod;

internal static class Protocol
{
    public const string SchemaVersion = "0.2";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static Envelope<T> CreateEnvelope<T>(string messageType, string? requestId, T payload)
    {
        return new Envelope<T>
        {
            SchemaVersion = SchemaVersion,
            MessageType = messageType,
            RequestId = requestId,
            CreatedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Payload = payload
        };
    }
}

internal sealed class Envelope<T>
{
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; set; } = Protocol.SchemaVersion;

    [JsonPropertyName("message_type")]
    public string MessageType { get; set; } = "";

    [JsonPropertyName("request_id")]
    public string? RequestId { get; set; }

    [JsonPropertyName("created_at_ms")]
    public long CreatedAtMs { get; set; }

    [JsonPropertyName("payload")]
    public T Payload { get; set; } = default!;
}

internal sealed class MovePayload
{
    [JsonPropertyName("actor_id")]
    public string ActorId { get; set; } = "companion-1";

    [JsonPropertyName("direction")]
    public string Direction { get; set; } = "";

    [JsonPropertyName("ticks")]
    public int Ticks { get; set; }
}

internal sealed class MoveToPayload
{
    [JsonPropertyName("actor_id")]
    public string ActorId { get; set; } = "companion-1";

    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }
}

internal sealed class FaceDirectionPayload
{
    [JsonPropertyName("actor_id")]
    public string ActorId { get; set; } = "companion-1";

    [JsonPropertyName("direction")]
    public string Direction { get; set; } = "";
}

internal sealed class UseToolPayload
{
    [JsonPropertyName("actor_id")]
    public string ActorId { get; set; } = "companion-1";

    [JsonPropertyName("tool")]
    public string Tool { get; set; } = "";

    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }
}

internal sealed class InteractPayload
{
    [JsonPropertyName("actor_id")]
    public string ActorId { get; set; } = "companion-1";

    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }
}

internal sealed class WarpPayload
{
    [JsonPropertyName("actor_id")]
    public string ActorId { get; set; } = "companion-1";

    [JsonPropertyName("location")]
    public string Location { get; set; } = "";

    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }
}

internal sealed class ObservePayload
{
    [JsonPropertyName("actor_id")]
    public string ActorId { get; set; } = "companion-1";

    [JsonPropertyName("radius")]
    public int Radius { get; set; } = 8;
}

internal sealed class AutoCombatPayload
{
    [JsonPropertyName("actor_id")]
    public string ActorId { get; set; } = "companion-1";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }
}

internal sealed class EatItemPayload
{
    [JsonPropertyName("actor_id")]
    public string ActorId { get; set; } = "companion-1";

    [JsonPropertyName("slot")]
    public int? Slot { get; set; }
}

internal sealed class SayPayload
{
    [JsonPropertyName("actor_id")]
    public string ActorId { get; set; } = "companion-1";

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";
}

internal sealed class BubblePayload
{
    [JsonPropertyName("actor_id")]
    public string ActorId { get; set; } = "companion-1";

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("duration_ms")]
    public long DurationMs { get; set; } = 3000;
}

internal sealed class CancelPayload
{
    [JsonPropertyName("actor_id")]
    public string ActorId { get; set; } = "companion-1";

    [JsonPropertyName("target_request_id")]
    public string TargetRequestId { get; set; } = "";
}

internal sealed class ErrorPayload
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "failed";

    [JsonPropertyName("action")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Action { get; set; }

    [JsonPropertyName("actor_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ActorId { get; set; }

    [JsonPropertyName("error")]
    public ErrorDetail Error { get; set; } = new();
}

internal sealed class ErrorDetail
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "unknown";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

internal sealed class ActionResultPayload
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "failed";

    [JsonPropertyName("action")]
    public string Action { get; set; } = "";

    [JsonPropertyName("actor_id")]
    public string ActorId { get; set; } = "companion-1";

    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Data { get; set; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ErrorDetail? Error { get; set; }
}

internal sealed class PingResultPayload
{
    [JsonPropertyName("actor_id")]
    public string ActorId { get; set; } = "companion-1";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "succeeded";

    [JsonPropertyName("action")]
    public string Action { get; set; } = "ping";

    [JsonPropertyName("mod_tick")]
    public long ModTick { get; set; }

    [JsonPropertyName("world_ready")]
    public bool WorldReady { get; set; }
}

internal sealed class MoveResultPayload
{
    [JsonPropertyName("actor_id")]
    public string ActorId { get; set; } = "companion-1";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "failed";

    [JsonPropertyName("action")]
    public string Action { get; set; } = "move_relative";

    [JsonPropertyName("direction")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Direction { get; set; }

    [JsonPropertyName("ticks")]
    public int Ticks { get; set; }

    [JsonPropertyName("target_tile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TileDto? TargetTile { get; set; }

    [JsonPropertyName("before_tile")]
    public TileDto? BeforeTile { get; set; }

    [JsonPropertyName("after_tile")]
    public TileDto? AfterTile { get; set; }

    [JsonPropertyName("moved")]
    public bool Moved { get; set; }

    [JsonPropertyName("world_ready")]
    public bool WorldReady { get; set; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ErrorDetail? Error { get; set; }
}

internal sealed class TileDto
{
    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }
}

internal sealed class SnapshotPayload
{
    [JsonPropertyName("latest_write_sequence")]
    public long LatestWriteSequence { get; set; }

    [JsonPropertyName("snapshot_sequence")]
    public long SnapshotSequence { get; set; }

    [JsonPropertyName("snapshot_index")]
    public int SnapshotIndex { get; set; } = -1;

    [JsonPropertyName("mod_version")]
    public string ModVersion { get; set; } = "0.2.0";

    [JsonPropertyName("game_tick")]
    public long GameTick { get; set; }

    [JsonPropertyName("world_ready")]
    public bool WorldReady { get; set; }

    [JsonPropertyName("game")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GameInfo? Game { get; set; }

    [JsonPropertyName("player")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PlayerInfo? Player { get; set; }

    [JsonPropertyName("companion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CompanionInfo? Companion { get; set; }
}

internal sealed class CompanionInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "companion-1";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "ai_companion";

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = "Companion";

    [JsonPropertyName("location")]
    public string Location { get; set; } = "";

    [JsonPropertyName("tile")]
    public TileDto Tile { get; set; } = new();

    [JsonPropertyName("facing_direction")]
    public string FacingDirection { get; set; } = "down";

    [JsonPropertyName("health")]
    public int Health { get; set; }

    [JsonPropertyName("max_health")]
    public int MaxHealth { get; set; }

    [JsonPropertyName("stamina")]
    public float Stamina { get; set; }

    [JsonPropertyName("max_stamina")]
    public float MaxStamina { get; set; }

    [JsonPropertyName("inventory_count")]
    public int InventoryCount { get; set; }

    [JsonPropertyName("inventory")]
    public List<InventoryItemInfo> Inventory { get; set; } = new();

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "direct";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "idle";

    [JsonPropertyName("current_action")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CurrentAction { get; set; }

    [JsonPropertyName("world_ready")]
    public bool WorldReady { get; set; }

    [JsonPropertyName("busy")]
    public bool Busy { get; set; }

    [JsonPropertyName("auto_combat")]
    public bool AutoCombat { get; set; }

    [JsonPropertyName("capabilities")]
    public List<string> Capabilities { get; set; } = new();
}

internal sealed class GameInfo
{
    [JsonPropertyName("year")]
    public int Year { get; set; }

    [JsonPropertyName("season")]
    public string Season { get; set; } = "";

    [JsonPropertyName("day")]
    public int Day { get; set; }

    [JsonPropertyName("time")]
    public int Time { get; set; }

    [JsonPropertyName("location")]
    public string Location { get; set; } = "";

    [JsonPropertyName("weather")]
    public string Weather { get; set; } = "unknown";
}

internal sealed class PlayerInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("location")]
    public string Location { get; set; } = "";

    [JsonPropertyName("tile")]
    public TileDto Tile { get; set; } = new();

    [JsonPropertyName("facing_direction")]
    public string FacingDirection { get; set; } = "down";

    [JsonPropertyName("health")]
    public int Health { get; set; }

    [JsonPropertyName("max_health")]
    public int MaxHealth { get; set; }

    [JsonPropertyName("stamina")]
    public float Stamina { get; set; }

    [JsonPropertyName("max_stamina")]
    public float MaxStamina { get; set; }

    [JsonPropertyName("money")]
    public int Money { get; set; }
}

internal sealed class InventoryItemInfo
{
    [JsonPropertyName("slot")]
    public int Slot { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("qualified_id")]
    public string QualifiedId { get; set; } = "";

    [JsonPropertyName("stack")]
    public int Stack { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "item";

    [JsonPropertyName("edibility")]
    public int Edibility { get; set; }
}

internal sealed class ObservationInfo
{
    [JsonPropertyName("location")]
    public string Location { get; set; } = "";

    [JsonPropertyName("center")]
    public TileDto Center { get; set; } = new();

    [JsonPropertyName("radius")]
    public int Radius { get; set; }

    [JsonPropertyName("tiles")]
    public List<ObservationTileInfo> Tiles { get; set; } = new();

    [JsonPropertyName("monsters")]
    public List<ObservationMonsterInfo> Monsters { get; set; } = new();

    [JsonPropertyName("npcs")]
    public List<ObservationNpcInfo> Npcs { get; set; } = new();
}

internal sealed class ObservationTileInfo
{
    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }

    [JsonPropertyName("passable")]
    public bool Passable { get; set; }

    [JsonPropertyName("water")]
    public bool Water { get; set; }

    [JsonPropertyName("terrain")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Terrain { get; set; }

    [JsonPropertyName("crop")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Crop { get; set; }

    [JsonPropertyName("crop_ready")]
    public bool CropReady { get; set; }

    [JsonPropertyName("water_state")]
    public int WaterState { get; set; } = -1;

    [JsonPropertyName("object_name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ObjectName { get; set; }

    [JsonPropertyName("object_type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ObjectType { get; set; }

    [JsonPropertyName("breakable")]
    public bool Breakable { get; set; }

    [JsonPropertyName("interactable")]
    public bool Interactable { get; set; }
}

internal sealed class ObservationMonsterInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }

    [JsonPropertyName("health")]
    public int Health { get; set; }

    [JsonPropertyName("max_health")]
    public int MaxHealth { get; set; }
}

internal sealed class ObservationNpcInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }
}
