using System.Text.Json;
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

        Monitor.Log($"Loaded. Bridge directory: {paths.Root}. AI actor: {CompanionController.Id}.", LogLevel.Info);
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        _modTick++;
        if (Context.IsWorldReady)
        {
            _companion?.EnsureSpawned();
            _companion?.EnsureSameLocationAsPlayer();
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
            if (processingPath is null)
                continue;

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

            if (request.MessageType != "action.request" || string.IsNullOrWhiteSpace(request.RequestId))
                throw new JsonException("request envelope is invalid");

            var action = request.Payload.TryGetProperty("action", out var actionElement)
                ? actionElement.GetString()
                : null;

            switch (action)
            {
                case "ping":
                    WritePingResult(request.RequestId);
                    Archive(processingPath);
                    break;
                case "move_relative":
                    StartMove(request, processingPath);
                    break;
                default:
                    WriteFailure(request.RequestId, "unsupported_action", $"unsupported action: {action ?? "<missing>"}");
                    Archive(processingPath);
                    break;
            }
        }
        catch (Exception error)
        {
            Monitor.Log($"Invalid request {fallbackRequestId}: {error.Message}", LogLevel.Warn);
            WriteFailure(fallbackRequestId, "invalid_request", error.Message);
            MoveToErrors(processingPath);
        }
    }

    private void StartMove(Envelope<JsonElement> request, string processingPath)
    {
        if (_moveExecutor is null || request.RequestId is null)
            return;

        var payload = JsonSerializer.Deserialize<MovePayload>(request.Payload.GetRawText(), Protocol.JsonOptions)
            ?? throw new JsonException("move payload is empty");
        if (!string.Equals(payload.ActorId, CompanionController.Id, StringComparison.OrdinalIgnoreCase))
        {
            WriteFailure(request.RequestId, "unsupported_actor", $"only {CompanionController.Id} is available in this demo");
            Archive(processingPath);
            return;
        }

        var started = _moveExecutor.TryStart(request.RequestId, payload.Direction, payload.Ticks, out var immediateFailure);
        if (!started)
        {
            WriteMoveResult(immediateFailure!);
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

    private void WritePingResult(string requestId)
    {
        var payload = new PingResultPayload
        {
            ActorId = CompanionController.Id,
            ModTick = _modTick,
            WorldReady = Context.IsWorldReady
        };
        WriteResult(requestId, payload);
    }

    private void WriteMoveResult(MoveCompletion completion)
    {
        var payload = new MoveResultPayload
        {
            ActorId = CompanionController.Id,
            Status = completion.Status,
            Direction = completion.Direction,
            Ticks = completion.Ticks,
            BeforeTile = completion.BeforeTile,
            AfterTile = completion.AfterTile,
            Moved = completion.Moved,
            WorldReady = completion.WorldReady,
            Error = completion.Error
        };
        WriteResult(completion.RequestId, payload);
    }

    private void WriteFailure(string requestId, string code, string message)
    {
        WriteResult(requestId, new ErrorPayload
        {
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
            ModVersion = this.ModManifest.Version.ToString(),
            GameTick = _modTick,
            WorldReady = Context.IsWorldReady,
            Game = Context.IsWorldReady ? new GameInfo
            {
                Year = Game1.year,
                Season = Game1.currentSeason,
                Day = Game1.dayOfMonth,
                Time = Game1.timeOfDay
            } : null,
            Player = Context.IsWorldReady && Game1.player is not null ? new PlayerInfo
            {
                Location = Game1.currentLocation?.Name ?? "",
                Tile = new TileDto
                {
                    X = (int)Game1.player.Tile.X,
                    Y = (int)Game1.player.Tile.Y
                }
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
        // Publishing a new history index also publishes a new latest state.
        // Keep the write sequence monotonic so `watch` observes this update
        // even when the two configured intervals do not align.
        _latestWriteSequence++;
        _latestSnapshotIndex = (_latestSnapshotIndex + 1) % _snapshotHistoryLimit;
        var snapshot = Protocol.CreateEnvelope(
            "snapshot",
            null,
            BuildSnapshot(_snapshotSequence, _latestSnapshotIndex));
        _store.ReplaceJson(
            Path.Combine(_store.Paths.Snapshots, $"snapshot-{_latestSnapshotIndex}.json"),
            snapshot);

        // latest is the complete newest state, and also publishes the newest ring index.
        WriteLatestSnapshot(advanceSequence: false);
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
