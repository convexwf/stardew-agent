using System.Text.Json;
using System.Text.Json.Serialization;

namespace StardewAgentMod;

internal static class Protocol
{
    public const string SchemaVersion = "0.1";

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

internal sealed class ErrorPayload
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "failed";

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
    public string Direction { get; set; } = "";

    [JsonPropertyName("ticks")]
    public int Ticks { get; set; }

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
    public string ModVersion { get; set; } = "0.1.0";

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

    [JsonPropertyName("world_ready")]
    public bool WorldReady { get; set; }

    [JsonPropertyName("busy")]
    public bool Busy { get; set; }
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
}

internal sealed class PlayerInfo
{
    [JsonPropertyName("location")]
    public string Location { get; set; } = "";

    [JsonPropertyName("tile")]
    public TileDto Tile { get; set; } = new();
}
