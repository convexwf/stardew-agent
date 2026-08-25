using System.Text.Json;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace StardewAgentMod;

internal sealed class ModEntry : Mod
{
    private BridgeFileStore? _store;
    private CompanionController? _companion;
    private MoveExecutor? _moveExecutor;
    private string? _activeProcessingPath;
    private long _latestWriteSequence;
    private long _snapshotSequence;
    private int _latestSnapshotIndex = -1;
    private int _latestWriteSeconds;
    private int _snapshotSeconds;
    private int _latestWriteIntervalSeconds;
    private int _snapshotHistoryIntervalSeconds;
    private int _snapshotHistoryLimit;
    private long _modTick;
    private bool _initialSnapshotWritten;

    public override void Entry(IModHelper helper)
    {
        var config = helper.ReadConfig<ModConfig>();
        _latestWriteIntervalSeconds = Math.Max(1, config.LatestWriteIntervalSeconds);
        _snapshotHistoryIntervalSeconds = Math.Max(1, config.SnapshotHistoryIntervalSeconds);
        _snapshotHistoryLimit = Math.Max(1, config.SnapshotHistoryLimit);
        if (string.IsNullOrWhiteSpace(config.BridgeDirectory))
        {
            config.BridgeDirectory = Path.Combine(helper.DirectoryPath, "bridge");
            helper.WriteConfig(config);
        }

        var paths = new BridgePaths(config.BridgeDirectory);
        paths.EnsureLayout();
        _store = new BridgeFileStore(paths);
        _store.NormalizeSnapshotSlots(_snapshotHistoryLimit);
        _latestWriteSequence = _store.GetLatestWriteSequence();
        var history = _store.GetLatestHistory();
        _snapshotSequence = history.Sequence;
        var latestIndex = _store.GetLatestSnapshotIndex();
        _latestSnapshotIndex = latestIndex.HasValue
            && latestIndex.Value >= 0
            && latestIndex.Value < _snapshotHistoryLimit
            ? latestIndex.Value
            : history.Index;
        _companion = new CompanionController(helper, Monitor);
        _moveExecutor = new MoveExecutor(_companion);

        helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
        helper.Events.GameLoop.OneSecondUpdateTicked += OnOneSecondUpdateTicked;
        helper.Events.GameLoop.DayStarted += OnDayStarted;
        helper.Events.GameLoop.DayEnding += OnDayEnding;
        helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
        helper.Events.Content.AssetRequested += OnAssetRequested;

        Monitor.Log($"Loaded. Bridge directory: {paths.Root}. AI actor: {CompanionController.Id}.", LogLevel.Info);
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (!e.NameWithoutLocale.IsEquivalentTo($"Portraits/{CompanionController.Id}"))
            return;

        e.LoadFrom(
            () => Helper.GameContent.Load<Texture2D>("Portraits/Abigail"),
            AssetLoadPriority.Exclusive);
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        _modTick++;
        if (Context.IsWorldReady)
        {
            _companion?.EnsureSpawned();
            if (!_initialSnapshotWritten && _companion?.IsSpawned == true)
            {
                WriteLatestSnapshot(advanceSequence: true);
                _initialSnapshotWritten = true;
            }
        }

        FinishMoveIfReady(_moveExecutor?.Tick());
        ProcessPendingRequests();
    }

    private void OnOneSecondUpdateTicked(object? sender, OneSecondUpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady)
            return;

        _latestWriteSeconds++;
        _snapshotSeconds++;
        if (_latestWriteSeconds >= _latestWriteIntervalSeconds)
        {
            _latestWriteSeconds = 0;
            WriteLatestSnapshot(advanceSequence: true);
        }
        if (_snapshotSeconds >= _snapshotHistoryIntervalSeconds)
        {
            _snapshotSeconds = 0;
            WriteHistorySnapshot();
        }
    }

    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        _companion?.WakeUp();
    }

    private void OnDayEnding(object? sender, DayEndingEventArgs e)
    {
        _companion?.SignalSleepReady();
    }

    private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        FinishMoveIfReady(_moveExecutor?.Cancel("world_not_ready", "the game returned to the title screen"));
        _companion?.Cleanup();
    }

    private void ProcessPendingRequests()
    {
        if (_store is null)
            return;

        IEnumerable<string> pendingFiles;
        try
        {
            pendingFiles = Directory.EnumerateFiles(_store.Paths.Pending, "*.json")
                .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
                .Take(8)
                .ToArray();
        }
        catch (Exception error)
        {
            Monitor.Log($"Failed to scan pending actions: {error.Message}", LogLevel.Error);
            return;
        }

        foreach (var pendingPath in pendingFiles)
        {
            var processingPath = _store.TryClaim(pendingPath);
            if (processingPath is not null)
                ProcessClaimedRequest(processingPath);
        }
    }

    private void ProcessClaimedRequest(string processingPath)
    {
        if (_store is null)
            return;

        var fallbackRequestId = Path.GetFileNameWithoutExtension(processingPath);
        try
        {
            var json = File.ReadAllText(processingPath);
            var request = JsonSerializer.Deserialize<Envelope<JsonElement>>(json, Protocol.JsonOptions)
                ?? throw new JsonException("request is empty");
            if (request.SchemaVersion != Protocol.SchemaVersion)
            {
                WriteFailure(request.RequestId ?? fallbackRequestId, "unsupported_schema", $"unsupported schema version: {request.SchemaVersion}", null, null);
                MoveToErrors(processingPath);
                return;
            }
            if (request.MessageType != "action.request" || string.IsNullOrWhiteSpace(request.RequestId))
                throw new JsonException("request envelope is invalid");

            var action = ReadString(request.Payload, "action");
            switch (action)
            {
                case "ping":
                    ExecutePing(request, processingPath);
                    break;
                case "move_relative":
                    StartMoveRelative(request, processingPath);
                    break;
                case "move_to":
                    StartMoveTo(request, processingPath);
                    break;
                case "face_direction":
                    ExecuteFaceDirection(request, processingPath);
                    break;
                case "use_tool":
                    ExecuteUseTool(request, processingPath);
                    break;
                case "interact":
                    ExecuteInteract(request, processingPath);
                    break;
                case "warp_to":
                    ExecuteWarp(request, processingPath);
                    break;
                case "observe":
                    ExecuteObserve(request, processingPath);
                    break;
                case "get_inventory":
                    ExecuteInventory(request, processingPath);
                    break;
                case "attack":
                    ExecuteAttack(request, processingPath);
                    break;
                case "cast_fishing_rod":
                    ExecuteCastFishingRod(request, processingPath);
                    break;
                case "set_auto_combat":
                    ExecuteSetAutoCombat(request, processingPath);
                    break;
                case "eat_item":
                    ExecuteEatItem(request, processingPath);
                    break;
                case "say":
                    ExecuteSay(request, processingPath);
                    break;
                case "bubble":
                    ExecuteBubble(request, processingPath);
                    break;
                case "cancel":
                    ExecuteCancel(request, processingPath);
                    break;
                default:
                    WriteFailure(request.RequestId!, "unsupported_action", $"unsupported action: {action ?? "<missing>"}", action, ReadActorId(request.Payload));
                    Archive(processingPath);
                    break;
            }
        }
        catch (Exception error)
        {
            Monitor.Log($"Invalid request {fallbackRequestId}: {error.Message}", LogLevel.Warn);
            WriteFailure(fallbackRequestId, "invalid_request", error.Message, null, null);
            MoveToErrors(processingPath);
        }
    }

    private void ExecutePing(Envelope<JsonElement> request, string processingPath)
    {
        if (!TryValidateActor(request, "ping", out var actorId))
        {
            Archive(processingPath);
            return;
        }
        WriteResult(request.RequestId!, new PingResultPayload
        {
            ActorId = actorId,
            ModTick = _modTick,
            WorldReady = Context.IsWorldReady
        });
        Archive(processingPath);
    }

    private void StartMoveRelative(Envelope<JsonElement> request, string processingPath)
    {
        if (!TryValidateActor(request, "move_relative", out _))
        {
            Archive(processingPath);
            return;
        }
        var payload = JsonSerializer.Deserialize<MovePayload>(request.Payload.GetRawText(), Protocol.JsonOptions)
            ?? throw new JsonException("move_relative payload is empty");
        if (_moveExecutor is null)
        {
            WriteFailure(request.RequestId!, "executor_unavailable", "the movement executor is unavailable", "move_relative", ReadActorId(request.Payload));
            Archive(processingPath);
            return;
        }
        var started = _moveExecutor.TryStartRelative(request.RequestId!, payload.Direction, payload.Ticks, out var failure);
        if (!started)
        {
            WriteMoveResult(failure!);
            Archive(processingPath);
            return;
        }
        _activeProcessingPath = processingPath;
    }

    private void StartMoveTo(Envelope<JsonElement> request, string processingPath)
    {
        if (!TryValidateActor(request, "move_to", out _))
        {
            Archive(processingPath);
            return;
        }
        var payload = JsonSerializer.Deserialize<MoveToPayload>(request.Payload.GetRawText(), Protocol.JsonOptions)
            ?? throw new JsonException("move_to payload is empty");
        if (_moveExecutor is null)
        {
            WriteFailure(request.RequestId!, "executor_unavailable", "the movement executor is unavailable", "move_to", ReadActorId(request.Payload));
            Archive(processingPath);
            return;
        }
        var started = _moveExecutor.TryStartTo(request.RequestId!, payload.X, payload.Y, out var failure);
        if (!started)
        {
            WriteMoveResult(failure!);
            Archive(processingPath);
            return;
        }
        _activeProcessingPath = processingPath;
    }

    private void FinishMoveIfReady(MoveCompletion? completion)
    {
        if (completion is null || _store is null || _activeProcessingPath is null)
            return;
        WriteMoveResult(completion);
        Archive(_activeProcessingPath);
        _activeProcessingPath = null;
    }

    private void ExecuteFaceDirection(Envelope<JsonElement> request, string processingPath)
    {
        var payload = Deserialize<FaceDirectionPayload>(request);
        if (!TryValidateActor(request, "face_direction", out var actorId))
        {
            Archive(processingPath);
            return;
        }
        ErrorDetail? error = null;
        var success = _companion is not null && _companion.TryFaceDirection(payload.Direction, out error);
        WriteActionResult(request.RequestId!, "face_direction", actorId, success, new { direction = payload.Direction }, error);
        Archive(processingPath);
    }

    private void ExecuteUseTool(Envelope<JsonElement> request, string processingPath)
    {
        var payload = Deserialize<UseToolPayload>(request);
        if (!TryValidateActor(request, "use_tool", out var actorId))
        {
            Archive(processingPath);
            return;
        }
        object? data = null;
        ErrorDetail? error = null;
        var success = _companion is not null && _companion.TryUseTool(payload.Tool, payload.X, payload.Y, out data, out error);
        WriteActionResult(request.RequestId!, "use_tool", actorId, success, data, error);
        Archive(processingPath);
    }

    private void ExecuteInteract(Envelope<JsonElement> request, string processingPath)
    {
        var payload = Deserialize<InteractPayload>(request);
        if (!TryValidateActor(request, "interact", out var actorId))
        {
            Archive(processingPath);
            return;
        }
        object? data = null;
        ErrorDetail? error = null;
        var success = _companion is not null && _companion.TryInteract(payload.X, payload.Y, out data, out error);
        WriteActionResult(request.RequestId!, "interact", actorId, success, data, error);
        Archive(processingPath);
    }

    private void ExecuteWarp(Envelope<JsonElement> request, string processingPath)
    {
        var payload = Deserialize<WarpPayload>(request);
        if (!TryValidateActor(request, "warp_to", out var actorId))
        {
            Archive(processingPath);
            return;
        }
        object? data = null;
        ErrorDetail? error = null;
        var success = _companion is not null && _companion.TryWarp(payload.Location, payload.X, payload.Y, out data, out error);
        WriteActionResult(request.RequestId!, "warp_to", actorId, success, data, error);
        Archive(processingPath);
    }

    private void ExecuteObserve(Envelope<JsonElement> request, string processingPath)
    {
        var payload = Deserialize<ObservePayload>(request);
        if (!TryValidateActor(request, "observe", out var actorId))
        {
            Archive(processingPath);
            return;
        }
        if (payload.Radius is < 1 or > 16)
        {
            WriteActionResult(request.RequestId!, "observe", actorId, false, null, new ErrorDetail { Code = "invalid_radius", Message = "radius must be between 1 and 16" });
            Archive(processingPath);
            return;
        }
        var observation = _companion?.GetObservation(payload.Radius);
        var success = observation is not null;
        WriteActionResult(request.RequestId!, "observe", actorId, success, success ? new { observation } : null, success ? null : new ErrorDetail { Code = "world_not_ready", Message = "the companion is not spawned" });
        Archive(processingPath);
    }

    private void ExecuteInventory(Envelope<JsonElement> request, string processingPath)
    {
        if (!TryValidateActor(request, "get_inventory", out var actorId))
        {
            Archive(processingPath);
            return;
        }
        var inventory = _companion?.GetInventory();
        var success = inventory is not null && _companion?.IsSpawned == true;
        WriteActionResult(request.RequestId!, "get_inventory", actorId, success, success ? new { inventory } : null, success ? null : new ErrorDetail { Code = "world_not_ready", Message = "the companion is not spawned" });
        Archive(processingPath);
    }

    private void ExecuteAttack(Envelope<JsonElement> request, string processingPath)
    {
        if (!TryValidateActor(request, "attack", out var actorId))
        {
            Archive(processingPath);
            return;
        }
        object? data = null;
        ErrorDetail? error = null;
        var success = _companion is not null && _companion.TryAttack(out data, out error);
        WriteActionResult(request.RequestId!, "attack", actorId, success, data, error);
        Archive(processingPath);
    }

    private void ExecuteCastFishingRod(Envelope<JsonElement> request, string processingPath)
    {
        if (!TryValidateActor(request, "cast_fishing_rod", out var actorId))
        {
            Archive(processingPath);
            return;
        }
        object? data = null;
        ErrorDetail? error = null;
        var success = _companion is not null && _companion.TryCastFishingRod(out data, out error);
        WriteActionResult(request.RequestId!, "cast_fishing_rod", actorId, success, data, error);
        Archive(processingPath);
    }

    private void ExecuteSetAutoCombat(Envelope<JsonElement> request, string processingPath)
    {
        var payload = Deserialize<AutoCombatPayload>(request);
        if (!TryValidateActor(request, "set_auto_combat", out var actorId))
        {
            Archive(processingPath);
            return;
        }
        ErrorDetail? error = null;
        var success = _companion is not null && _companion.TrySetAutoCombat(payload.Enabled, out error);
        WriteActionResult(request.RequestId!, "set_auto_combat", actorId, success, new { enabled = payload.Enabled }, error);
        Archive(processingPath);
    }

    private void ExecuteEatItem(Envelope<JsonElement> request, string processingPath)
    {
        var payload = Deserialize<EatItemPayload>(request);
        if (!TryValidateActor(request, "eat_item", out var actorId))
        {
            Archive(processingPath);
            return;
        }
        object? data = null;
        ErrorDetail? error = null;
        var success = _companion is not null && _companion.TryEatItem(payload.Slot, out data, out error);
        WriteActionResult(request.RequestId!, "eat_item", actorId, success, data, error);
        Archive(processingPath);
    }

    private void ExecuteSay(Envelope<JsonElement> request, string processingPath)
    {
        var payload = Deserialize<SayPayload>(request);
        if (!TryValidateActor(request, "say", out var actorId))
        {
            Archive(processingPath);
            return;
        }

        var text = payload.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            WriteActionResult(request.RequestId!, "say", actorId, false, null, new ErrorDetail
            {
                Code = "invalid_text",
                Message = "text must not be empty"
            });
            Archive(processingPath);
            return;
        }

        if (text.Length > 500)
        {
            WriteActionResult(request.RequestId!, "say", actorId, false, null, new ErrorDetail
            {
                Code = "text_too_long",
                Message = "text must be at most 500 characters"
            });
            Archive(processingPath);
            return;
        }

        if (!Context.IsWorldReady || Game1.chatBox is null)
        {
            WriteActionResult(request.RequestId!, "say", actorId, false, null, new ErrorDetail
            {
                Code = "world_not_ready",
                Message = "the game chat window is not ready"
            });
            Archive(processingPath);
            return;
        }

        Game1.chatBox.addMessage(text, Microsoft.Xna.Framework.Color.Gold);
        WriteActionResult(request.RequestId!, "say", actorId, true, new
        {
            text,
            channel = "chat"
        }, null);
        Archive(processingPath);
    }

    private void ExecuteBubble(Envelope<JsonElement> request, string processingPath)
    {
        var payload = Deserialize<BubblePayload>(request);
        if (!TryValidateActor(request, "bubble", out var actorId))
        {
            Archive(processingPath);
            return;
        }

        var text = payload.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            WriteActionResult(request.RequestId!, "bubble", actorId, false, null, new ErrorDetail
            {
                Code = "invalid_text",
                Message = "text must not be empty"
            });
            Archive(processingPath);
            return;
        }

        if (text.Length > 500)
        {
            WriteActionResult(request.RequestId!, "bubble", actorId, false, null, new ErrorDetail
            {
                Code = "text_too_long",
                Message = "text must be at most 500 characters"
            });
            Archive(processingPath);
            return;
        }

        if (payload.DurationMs is < 250 or > 30_000)
        {
            WriteActionResult(request.RequestId!, "bubble", actorId, false, null, new ErrorDetail
            {
                Code = "invalid_duration",
                Message = "duration_ms must be between 250 and 30000"
            });
            Archive(processingPath);
            return;
        }

        ErrorDetail? error = null;
        var success = _companion is not null
            && _companion.TryShowBubble(text, (int)payload.DurationMs, out error);
        WriteActionResult(request.RequestId!, "bubble", actorId, success, success
            ? new { text, channel = "bubble", duration_ms = payload.DurationMs }
            : null, error);
        Archive(processingPath);
    }

    private void ExecuteCancel(Envelope<JsonElement> request, string processingPath)
    {
        var payload = Deserialize<CancelPayload>(request);
        if (!TryValidateActor(request, "cancel", out var actorId))
        {
            Archive(processingPath);
            return;
        }
        if (!string.Equals(_activeProcessingPath is null ? null : Path.GetFileNameWithoutExtension(_activeProcessingPath), payload.TargetRequestId, StringComparison.OrdinalIgnoreCase))
        {
            WriteActionResult(request.RequestId!, "cancel", actorId, false, null, new ErrorDetail { Code = "request_not_active", Message = $"request is not the active movement: {payload.TargetRequestId}" });
            Archive(processingPath);
            return;
        }

        var completion = _moveExecutor?.Cancel("cancelled", "movement cancelled by a new cancel request");
        FinishMoveIfReady(completion);
        WriteActionResult(request.RequestId!, "cancel", actorId, true, new { target_request_id = payload.TargetRequestId, cancelled = true }, null);
        Archive(processingPath);
    }

    private bool TryValidateActor(Envelope<JsonElement> request, string action, out string actorId)
    {
        actorId = ReadString(request.Payload, "actor_id") ?? "";
        if (string.IsNullOrWhiteSpace(actorId))
        {
            WriteFailure(request.RequestId!, "invalid_actor", "actor_id is required", action, null);
            return false;
        }
        if (string.Equals(actorId, CompanionController.Id, StringComparison.OrdinalIgnoreCase))
            return true;
        WriteFailure(request.RequestId!, "unsupported_actor", $"only {CompanionController.Id} is available in this demo", action, actorId);
        return false;
    }

    private static T Deserialize<T>(Envelope<JsonElement> request)
    {
        return JsonSerializer.Deserialize<T>(request.Payload.GetRawText(), Protocol.JsonOptions)
            ?? throw new JsonException($"{typeof(T).Name} payload is empty");
    }

    private static string ReadActorId(JsonElement payload)
    {
        return ReadString(payload, "actor_id") ?? "";
    }

    private static string? ReadString(JsonElement payload, string property)
    {
        return payload.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;
    }

    private void WriteMoveResult(MoveCompletion completion)
    {
        WriteResult(completion.RequestId, new MoveResultPayload
        {
            ActorId = CompanionController.Id,
            Status = completion.Status,
            Action = completion.Action,
            Direction = completion.Direction,
            Ticks = completion.Ticks,
            TargetTile = completion.TargetTile,
            BeforeTile = completion.BeforeTile,
            AfterTile = completion.AfterTile,
            Moved = completion.Moved,
            WorldReady = completion.WorldReady,
            Error = completion.Error
        });
    }

    private void WriteActionResult(string requestId, string action, string actorId, bool success, object? data, ErrorDetail? error)
    {
        WriteResult(requestId, new ActionResultPayload
        {
            Status = success ? "succeeded" : "failed",
            Action = action,
            ActorId = actorId,
            Data = data,
            Error = error
        });
    }

    private void WriteFailure(string requestId, string code, string message, string? action, string? actorId)
    {
        WriteResult(requestId, new ErrorPayload
        {
            Action = action,
            ActorId = actorId,
            Error = new ErrorDetail { Code = code, Message = message }
        });
    }

    private void WriteResult<T>(string requestId, T payload)
    {
        if (_store is null)
            return;
        var result = Protocol.CreateEnvelope("action.result", requestId, payload);
        _store.WriteJson(Path.Combine(_store.Paths.Results, $"{requestId}.json"), result);
    }

    private SnapshotPayload BuildSnapshot(long snapshotSequence, int snapshotIndex)
    {
        return new SnapshotPayload
        {
            LatestWriteSequence = _latestWriteSequence,
            SnapshotSequence = snapshotSequence,
            SnapshotIndex = snapshotIndex,
            ModVersion = ModManifest.Version.ToString(),
            GameTick = _modTick,
            WorldReady = Context.IsWorldReady,
            Game = Context.IsWorldReady ? new GameInfo
            {
                Year = Game1.year,
                Season = Game1.currentSeason,
                Day = Game1.dayOfMonth,
                Time = Game1.timeOfDay,
                Location = Game1.currentLocation?.Name ?? "",
                Weather = GetWeather()
            } : null,
            Player = Context.IsWorldReady && Game1.player is not null ? new PlayerInfo
            {
                Name = Game1.player.Name,
                Location = Game1.player.currentLocation?.Name ?? "",
                Tile = new TileDto { X = (int)Game1.player.Tile.X, Y = (int)Game1.player.Tile.Y },
                FacingDirection = FacingName(Game1.player.FacingDirection),
                Health = Game1.player.health,
                MaxHealth = Game1.player.maxHealth,
                Stamina = Game1.player.Stamina,
                MaxStamina = Game1.player.MaxStamina,
                Money = Game1.player.Money
            } : null,
            Companion = _companion?.GetInfo()
        };
    }

    private void WriteLatestSnapshot(bool advanceSequence)
    {
        if (_store is null)
            return;
        if (advanceSequence)
            _latestWriteSequence++;
        var latest = Protocol.CreateEnvelope("snapshot", null, BuildSnapshot(_snapshotSequence, _latestSnapshotIndex));
        _store.ReplaceJson(Path.Combine(_store.Paths.Snapshots, "snapshot-latest.json"), latest);
    }

    private void WriteHistorySnapshot()
    {
        if (_store is null)
            return;
        _snapshotSequence++;
        _latestWriteSequence++;
        _latestSnapshotIndex = (_latestSnapshotIndex + 1) % _snapshotHistoryLimit;
        var snapshot = Protocol.CreateEnvelope("snapshot", null, BuildSnapshot(_snapshotSequence, _latestSnapshotIndex));
        _store.ReplaceJson(Path.Combine(_store.Paths.Snapshots, $"snapshot-{_latestSnapshotIndex}.json"), snapshot);
        WriteLatestSnapshot(advanceSequence: false);
    }

    private static string GetWeather()
    {
        if (Game1.isLightning) return "storm";
        if (Game1.isRaining) return "rain";
        if (Game1.isSnowing) return "snow";
        if (Game1.isDebrisWeather) return "windy";
        return "sunny";
    }

    private static string FacingName(int direction)
    {
        return direction switch
        {
            0 => "up",
            1 => "right",
            2 => "down",
            3 => "left",
            _ => "down"
        };
    }

    private void Archive(string processingPath)
    {
        try
        {
            _store?.Archive(processingPath);
        }
        catch (Exception error)
        {
            Monitor.Log($"Failed to archive {processingPath}: {error.Message}", LogLevel.Warn);
        }
    }

    private void MoveToErrors(string processingPath)
    {
        try
        {
            _store?.MoveToErrors(processingPath);
        }
        catch (Exception error)
        {
            Monitor.Log($"Failed to move {processingPath} to errors: {error.Message}", LogLevel.Warn);
        }
    }
}
