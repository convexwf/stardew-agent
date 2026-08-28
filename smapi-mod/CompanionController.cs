using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Monsters;
using StardewValley.Objects;
using StardewValley.Pathfinding;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;

namespace StardewAgentMod;

internal sealed class CompanionController
{
    public const string Id = "companion-1";
    public const string DisplayName = "Companion";

    private readonly IModHelper _helper;
    private readonly IMonitor _monitor;
    private CompanionNpc? _visual;
    private BotFarmer? _shadow;
    private ActiveMove? _activeMove;
    private ToolActionTask? _toolAction;
    private bool _autoCombat;
    private bool _fishingActive;
    private string? _fishingRequestId;
    private int _autoCombatCooldown;
    private bool _followActive;
    private string? _followRequestId;
    private int _followDistance;
    private int _followRepathCooldown;
    private int _followBlockedTicks;
    private Point _followLastTargetTile;
    private string _followState = "idle";
    private int _followWarpCount;
    private string? _followLastWarpLocation;
    private readonly IReadOnlyDictionary<string, string> _bubbleTemplates;
    private bool _modeActive;
    private string? _modeRequestId;
    private string? _modeName;
    private string _modeState = "idle";
    private Point _modeTargetTile;
    private Point _modePathTile;
    private int _modePhaseTicks;
    private int _modeRetries;
    private int _modeCompletedCount;
    private int _modeBlockedTicks;
    private float _modeBeforeHealth;
    private string? _modeLastNotice;
    private int _modeBubbleCooldown;

    private const int FollowMaxDistance = 8;
    private const int FollowEmergencyWarpDistance = 10;
    private const int FollowRepathInterval = 15;
    private const int FollowBlockedTimeout = 60;
    private const int ModePathTimeout = 180;
    private const int ModeVerificationDelay = 12;
    private const int ModeMaxRetries = 3;
    private const int ModeBubbleCooldownTicks = 300;
    private const int SwingPresentationTicks = 20;
    private const int MeleePresentationTicks = 18;
    private const int WaterPresentationTicks = 26;
    private const int CastPresentationTicks = 30;
    private const int ToolPathTimeout = 180;
    private const int ToolVerificationDelay = 2;

    private static readonly HashSet<string> SupportedModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "chop_trees",
        "water_crops",
        "harvest_crops",
        "plant_crops",
        "mine",
        "fish"
    };

    public CompanionController(IModHelper helper, IMonitor monitor, IReadOnlyDictionary<string, string>? bubbleTemplates = null)
    {
        _helper = helper;
        _monitor = monitor;
        _bubbleTemplates = bubbleTemplates ?? new Dictionary<string, string>();
    }

    public bool IsSpawned => _visual is not null && _shadow is not null;

    public bool IsBusy => _activeMove is not null || _toolAction is not null || _fishingActive || _autoCombat || _followActive || _modeActive || HasSpeechBubble;

    public bool IsFishingActive => _fishingActive;

    public bool HasSpeechBubble => _visual?.HasTextAboveHead == true;

    public string? CurrentAction => _activeMove?.Action
        ?? (_toolAction is not null ? "use_tool" : null)
        ?? (_fishingActive ? "cast_fishing_rod" : null)
        ?? (_modeActive ? _modeName : null)
        ?? (_followActive ? "follow" : null)
        ?? (_autoCombat ? "set_auto_combat" : null)
        ?? (HasSpeechBubble ? "bubble" : null);

    public bool AutoCombat => _autoCombat;

    public bool IsFollowing => _followActive;

    public bool IsModeActive => _modeActive;

    public void EnsureSpawned()
    {
        if (IsSpawned || !Context.IsWorldReady || Game1.currentLocation is null || Game1.player is null)
            return;

        var location = Game1.currentLocation;
        var position = FindSpawnPosition(location);
        var portrait = _helper.GameContent.Load<Texture2D>("Portraits/Abigail");
        _visual = new CompanionNpc(
            new AnimatedSprite("Characters\\Abigail", 0, 16, 32),
            position,
            location.Name,
            2,
            Id,
            portrait)
        {
            displayName = DisplayName
        };
        location.addCharacter(_visual);

        _shadow = new BotFarmer
        {
            UniqueMultiplayerID = _helper.Multiplayer.GetNewID(),
            Name = Id + "-shadow",
            displayName = DisplayName,
            Speed = 2,
            Stamina = Farmer.startingStamina,
            MaxItems = 36
        };
        foreach (var tool in Farmer.initialTools())
            _shadow.Items.Add(tool);
        while (_shadow.Items.Count < _shadow.MaxItems)
            _shadow.Items.Add(null);

        SyncShadow();
        _monitor.Log(
            $"Spawned {DisplayName} in {location.Name} at pixel ({position.X:0},{position.Y:0}), "
            + $"tile ({_visual.Tile.X:0.0},{_visual.Tile.Y:0.0}), visible={!_visual.IsInvisible}, "
            + $"sprite_loaded={_visual.Sprite?.Texture is not null}.",
            LogLevel.Info);
    }

    private static Vector2 FindSpawnPosition(GameLocation location)
    {
        var playerTile = Game1.player.Tile;
        var candidates = new[]
        {
            new Vector2(playerTile.X + 1, playerTile.Y),
            new Vector2(playerTile.X - 1, playerTile.Y),
            new Vector2(playerTile.X, playerTile.Y - 1),
            new Vector2(playerTile.X, playerTile.Y + 1)
        };

        foreach (var tile in candidates)
        {
            var mapTile = new xTile.Dimensions.Location((int)tile.X, (int)tile.Y);
            if (location.isTilePassable(mapTile, Game1.viewport))
                return tile * Game1.tileSize;
        }

        return Game1.player.Position + new Vector2(Game1.tileSize, 0f);
    }

    public MoveCompletion? Tick()
    {
        if (!IsSpawned || _visual is null || _shadow is null)
            return null;

        SyncShadow();
        TickAutoCombat();
        TickActionPresentation();

        if (_activeMove is null)
            return null;

        _activeMove.ElapsedTicks++;
        var current = ReadTile();
        var reached = current.X == _activeMove.Target.X && current.Y == _activeMove.Target.Y;
        var pathEnded = _visual.controller is null && _activeMove.ElapsedTicks > 2;
        var timedOut = _activeMove.ElapsedTicks > Math.Max(120, _activeMove.Ticks * 120);
        if (!reached && !pathEnded && !timedOut)
            return null;

        var active = _activeMove;
        _activeMove = null;
        _visual.controller = null;
        var after = ReadTile();
        var moved = active.Before.X != after.X || active.Before.Y != after.Y;
        return new MoveCompletion
        {
            RequestId = active.RequestId,
            Action = active.Action,
            Direction = active.Direction,
            Ticks = active.Ticks,
            TargetTile = new TileDto { X = active.Target.X, Y = active.Target.Y },
            Status = reached && !timedOut ? "succeeded" : "blocked",
            BeforeTile = active.Before,
            AfterTile = after,
            Moved = moved,
            WorldReady = Context.IsWorldReady,
            Error = timedOut
                ? new ErrorDetail { Code = "movement_timeout", Message = "the companion did not reach the target tile" }
                : null
        };
    }

    public bool TryStartFollow(string requestId, string targetActorId, int distance, out object? data, out ErrorDetail? error)
    {
        data = null;
        error = null;
        if (!string.Equals(targetActorId, "player", StringComparison.OrdinalIgnoreCase))
        {
            error = new ErrorDetail { Code = "unsupported_follow_target", Message = "the only supported follow target is player" };
            return false;
        }
        if (distance is < 1 or > FollowMaxDistance)
        {
            error = new ErrorDetail { Code = "invalid_distance", Message = $"distance must be between 1 and {FollowMaxDistance}" };
            return false;
        }

        EnsureSpawned();
        if (!Context.IsWorldReady || Game1.player?.currentLocation is null || !IsSpawned)
        {
            error = new ErrorDetail { Code = "follow_target_unavailable", Message = "the player or current world is unavailable" };
            return false;
        }
        if (_activeMove is not null || _toolAction is not null || _fishingActive || _autoCombat || HasSpeechBubble || _followActive)
        {
            error = new ErrorDetail { Code = "busy", Message = "the companion is already running another action" };
            return false;
        }

        _followActive = true;
        _followRequestId = requestId;
        _followDistance = distance;
        _followRepathCooldown = 0;
        _followBlockedTicks = 0;
        _followLastTargetTile = Point.Zero;
        _followState = "following";
        _followWarpCount = 0;
        _followLastWarpLocation = null;
        data = new { target_actor_id = "player", distance };
        return true;
    }

    public ActionCompletion? TickFollow()
    {
        if (!_followActive || _followRequestId is null)
            return null;
        if (!Context.IsWorldReady || Game1.player?.currentLocation is null || _visual is null || _shadow is null)
            return FinishFollow("failed", "follow_target_unavailable", "the player or current world is unavailable");

        var player = Game1.player;
        var playerLocation = player.currentLocation!;
        var playerTile = new Point((int)player.Tile.X, (int)player.Tile.Y);
        if (_visual.currentLocation is null || !string.Equals(_visual.currentLocation.Name, playerLocation.Name, StringComparison.Ordinal))
        {
            if (!TryWarpToFollowTarget(playerLocation, out var warpError))
                return FinishFollow("failed", warpError?.Code ?? "follow_warp_failed", warpError?.Message ?? "the companion could not warp to the player");
            _followLastTargetTile = playerTile;
            return null;
        }

        SyncShadow();
        if (_followRepathCooldown > 0)
            _followRepathCooldown--;

        var distance = Vector2.Distance(_visual.Tile, player.Tile);
        if (distance <= _followDistance)
        {
            _visual.controller = null;
            _followState = "waiting";
            _followBlockedTicks = 0;
            _followLastTargetTile = playerTile;
            return null;
        }

        var targetMoved = _followLastTargetTile == Point.Zero
            || Math.Abs(playerTile.X - _followLastTargetTile.X) >= 2
            || Math.Abs(playerTile.Y - _followLastTargetTile.Y) >= 2;
        if (targetMoved && _visual.controller is not null && _followRepathCooldown <= 0)
            _visual.controller = null;

        if (distance > FollowEmergencyWarpDistance)
        {
            if (!TryWarpToFollowTarget(playerLocation, out var warpError))
                return FinishFollow("failed", warpError?.Code ?? "follow_warp_failed", warpError?.Message ?? "the companion could not warp near the player");
            _followLastTargetTile = playerTile;
            return null;
        }

        if (_visual.controller is null)
        {
            if (_followRepathCooldown > 0)
            {
                _followBlockedTicks++;
            }
            else if (!TryStartFollowPath(playerLocation, playerTile, out var pathError))
            {
                _followState = "blocked";
                if (!TryWarpToFollowTarget(playerLocation, out var warpError))
                    return FinishFollow("failed", "follow_path_blocked", pathError?.Message ?? warpError?.Message ?? "the companion could not follow the player");
            }

            if (_followBlockedTicks > FollowBlockedTimeout)
            {
                _followState = "blocked";
                if (!TryWarpToFollowTarget(playerLocation, out var warpError))
                    return FinishFollow("failed", "follow_path_blocked", warpError?.Message ?? "the companion path is blocked");
                _followLastTargetTile = playerTile;
            }
        }

        _followState = "following";
        _followLastTargetTile = playerTile;
        return null;
    }

    public ActionCompletion? CancelFollow(string code, string message)
    {
        if (!_followActive || _followRequestId is null)
            return null;

        ClearActionPresentation();
        var completion = new ActionCompletion
        {
            RequestId = _followRequestId,
            Action = "follow",
            Status = "cancelled",
            Data = GetFollowInfo(),
            Error = new ErrorDetail { Code = code, Message = message }
        };
        ClearFollowState();
        return completion;
    }

    private ActionCompletion FinishFollow(string status, string code, string message)
    {
        var completion = new ActionCompletion
        {
            RequestId = _followRequestId ?? "unknown",
            Action = "follow",
            Status = status,
            Data = GetFollowInfo(),
            Error = new ErrorDetail { Code = code, Message = message }
        };
        ClearFollowState();
        return completion;
    }

    private bool TryStartFollowPath(GameLocation location, Point playerTile, out ErrorDetail? error)
    {
        error = null;
        if (_visual is null)
        {
            error = new ErrorDetail { Code = "follow_target_unavailable", Message = "the companion is not spawned" };
            return false;
        }

        var target = FindFollowTargetTile(location, playerTile, _followDistance);
        if (target is null)
        {
            error = new ErrorDetail { Code = "follow_path_blocked", Message = "no passable tile was found near the player" };
            return false;
        }

        try
        {
            _visual.controller = new PathFindController(_visual, location, target.Value, 2);
            _followRepathCooldown = FollowRepathInterval;
            _followBlockedTicks = 0;
            _followState = "following";
            return true;
        }
        catch (Exception exception)
        {
            error = new ErrorDetail { Code = "follow_path_blocked", Message = exception.Message };
            return false;
        }
    }

    private bool TryWarpToFollowTarget(GameLocation location, out ErrorDetail? error)
    {
        error = null;
        if (_visual is null || _shadow is null || Game1.player is null)
        {
            error = new ErrorDetail { Code = "follow_warp_failed", Message = "the companion or player is unavailable" };
            return false;
        }
        ClearActionPresentation();

        var target = FindFollowTargetTile(location, new Point((int)Game1.player.Tile.X, (int)Game1.player.Tile.Y), _followDistance);
        if (target is null)
        {
            error = new ErrorDetail { Code = "follow_warp_failed", Message = "no passable tile was found near the player" };
            return false;
        }

        _visual.currentLocation?.characters.Remove(_visual);
        _visual.controller = null;
        var position = new Vector2(target.Value.X * Game1.tileSize, target.Value.Y * Game1.tileSize);
        _visual.Position = position;
        _visual.currentLocation = location;
        location.addCharacter(_visual);
        _shadow.Position = position;
        _shadow.currentLocation = location;
        SyncShadow();
        _followState = "warping";
        _followWarpCount++;
        _followLastWarpLocation = location.Name;
        _followRepathCooldown = FollowRepathInterval;
        _followBlockedTicks = 0;
        _monitor.Log($"Warped {DisplayName} to follow the player in {location.Name} at ({target.Value.X},{target.Value.Y}).", LogLevel.Debug);
        return true;
    }

    private static Point? FindFollowTargetTile(GameLocation location, Point playerTile, int preferredDistance)
    {
        var distance = Math.Max(1, preferredDistance);
        var offsets = new[]
        {
            new Point(distance, 0), new Point(-distance, 0), new Point(0, -distance), new Point(0, distance),
            new Point(1, 0), new Point(-1, 0), new Point(0, -1), new Point(0, 1)
        };
        foreach (var offset in offsets)
        {
            var tile = new Point(playerTile.X + offset.X, playerTile.Y + offset.Y);
            var mapTile = new xTile.Dimensions.Location(tile.X, tile.Y);
            if (location.isTilePassable(mapTile, Game1.viewport))
                return tile;
        }
        return null;
    }

    private void ClearFollowState()
    {
        if (_visual is not null)
            _visual.controller = null;
        _followActive = false;
        _followRequestId = null;
        _followDistance = 0;
        _followRepathCooldown = 0;
        _followBlockedTicks = 0;
        _followLastTargetTile = Point.Zero;
        _followState = "idle";
        _followWarpCount = 0;
        _followLastWarpLocation = null;
    }

    private FollowInfo? GetFollowInfo()
    {
        if (!_followActive)
            return null;
        return new FollowInfo
        {
            TargetActorId = "player",
            Distance = _followDistance,
            State = _followState,
            TargetLocation = Game1.player?.currentLocation?.Name,
            TargetTile = Game1.player is null ? null : new TileDto { X = (int)Game1.player.Tile.X, Y = (int)Game1.player.Tile.Y },
            WarpCount = _followWarpCount,
            LastWarpLocation = _followLastWarpLocation
        };
    }

    public bool TryStartMode(string requestId, string mode, out object? data, out ErrorDetail? error)
    {
        data = null;
        error = null;
        var normalizedMode = (mode ?? "").Trim().ToLowerInvariant();
        if (!SupportedModes.Contains(normalizedMode))
        {
            error = new ErrorDetail
            {
                Code = "unsupported_mode",
                Message = $"unsupported mode: {mode}"
            };
            return false;
        }

        EnsureSpawned();
        if (!Context.IsWorldReady || _visual is null || _shadow is null || _visual.currentLocation is null)
        {
            error = new ErrorDetail { Code = "world_not_ready", Message = "the companion is not spawned" };
            return false;
        }
        if (_activeMove is not null || _toolAction is not null || _fishingActive || _autoCombat || _followActive || _modeActive || HasSpeechBubble)
        {
            error = new ErrorDetail { Code = "busy", Message = "the companion is already running another action" };
            return false;
        }

        _modeActive = true;
        _modeRequestId = requestId;
        _modeName = normalizedMode;
        _modeState = "scanning";
        _modeTargetTile = Point.Zero;
        _modePathTile = Point.Zero;
        _modePhaseTicks = 0;
        _modeRetries = 0;
        _modeCompletedCount = 0;
        _modeBlockedTicks = 0;
        _modeBeforeHealth = 0;
        _modeLastNotice = null;
        _modeBubbleCooldown = 0;
        data = new { mode = normalizedMode, state = _modeState };
        return true;
    }

    public ActionCompletion? TickAutonomousMode()
    {
        if (!_modeActive || _modeRequestId is null || _modeName is null || _visual is null || _shadow is null)
            return null;
        if (!Context.IsWorldReady || _visual.currentLocation is null)
            return FinishMode("failed", "world_not_ready", "the game world is no longer available");

        if (_modeBubbleCooldown > 0)
            _modeBubbleCooldown--;
        SyncShadow();

        if (_toolAction is not null)
        {
            var toolCompletion = AdvanceToolAction();
            if (toolCompletion is null)
                return null;
            if (toolCompletion.Status is not ("succeeded" or "completed"))
            {
                PauseMode("ModeActionFailed", _modeName, toolCompletion.Error?.Message ?? "the tool action failed");
                return null;
            }

            _modeState = "verifying";
            _modePhaseTicks = 0;
            return null;
        }

        if (_modeState == "paused")
        {
            _modePhaseTicks++;
            if (_modePhaseTicks < ModeBubbleCooldownTicks)
                return null;
            _modeState = "scanning";
            _modePhaseTicks = 0;
        }

        if (_modeState == "moving")
            return TickModeMovement();
        if (_modeState == "acting")
            return TickModeAction();
        if (_modeState == "verifying")
            return TickModeVerification();
        if (_modeState == "fishing")
            return TickModeFishing();

        return TickModeScan();
    }

    public ActionCompletion? CancelMode(string code, string message)
    {
        if (!_modeActive || _modeRequestId is null)
            return null;

        CancelToolActionInternal();
        if (_fishingActive)
        {
            _fishingActive = false;
            _fishingRequestId = null;
        }
        return FinishMode("cancelled", code, message);
    }

    public ModeInfo? GetModeInfo()
    {
        if (!_modeActive || _modeName is null)
            return null;
        return new ModeInfo
        {
            Id = _modeName,
            State = _modeState,
            TargetTile = _modeState is "moving" or "acting" or "tool_action" or "verifying" or "fishing"
                ? new TileDto { X = _modeTargetTile.X, Y = _modeTargetTile.Y }
                : null,
            CompletedCount = _modeCompletedCount,
            LastNotice = _modeLastNotice
        };
    }

    private ActionCompletion? TickModeScan()
    {
        if (_modeName is null || _visual?.currentLocation is null)
            return FinishMode("failed", "world_not_ready", "the companion location is unavailable");

        if (_modeName == "mine" && _visual.currentLocation is not MineShaft)
        {
            PauseMode("ModeActionFailed", "mine", "the companion must be inside a mine shaft");
            return null;
        }

        var target = FindModeTarget();
        if (target is null)
        {
            if (_modeName == "fish")
            {
                PauseMode("NoFishingWater", "fish", "no fishable water was found nearby");
                return null;
            }
            return FinishMode("succeeded", null, null);
        }

        if (!HasModePrerequisites())
            return null;

        _modeTargetTile = target.Value;
        if (IsToolMode())
        {
            _modeRetries = 0;
            _modeBlockedTicks = 0;
            _modePhaseTicks = 0;
            _modeState = "acting";
            return null;
        }

        _modePathTile = FindModeApproachTile(_visual.currentLocation, _modeTargetTile) ?? Point.Zero;
        if (_modePathTile == Point.Zero)
        {
            PauseMode("PathBlocked", _modeName, "no passable tile was found near the target");
            return null;
        }

        _modeRetries = 0;
        _modeBlockedTicks = 0;
        _modePhaseTicks = 0;
        if (ReadTile().X == _modePathTile.X && ReadTile().Y == _modePathTile.Y)
        {
            _modeState = "acting";
            return null;
        }

        try
        {
            _visual.controller = new PathFindController(_visual, _visual.currentLocation, _modePathTile, 2);
            _modeState = "moving";
            return null;
        }
        catch (Exception exception)
        {
            _monitor.Log($"Mode pathfinding failed: {exception.Message}", LogLevel.Debug);
            PauseMode("PathBlocked", _modeName, exception.Message);
            return null;
        }
    }

    private ActionCompletion? TickModeMovement()
    {
        if (_visual is null || _modeName is null)
            return FinishMode("failed", "world_not_ready", "the companion is unavailable");

        _modePhaseTicks++;
        if (_visual.controller is not null && _modePhaseTicks <= ModePathTimeout)
            return null;

        var current = ReadTile();
        if (Math.Abs(current.X - _modePathTile.X) <= 1 && Math.Abs(current.Y - _modePathTile.Y) <= 1)
        {
            _visual.controller = null;
            _modeState = "acting";
            _modePhaseTicks = 0;
            return null;
        }

        _visual.controller = null;
        _modeBlockedTicks++;
        if (_modeBlockedTicks >= ModeMaxRetries)
            PauseMode("PathBlocked", _modeName, "the companion could not reach the target");
        else
        {
            _modeState = "scanning";
            _modePhaseTicks = 0;
        }
        return null;
    }

    private ActionCompletion? TickModeAction()
    {
        if (_modeName is null)
            return FinishMode("failed", "world_not_ready", "the mode is unavailable");

        ErrorDetail? error = null;
        var success = _modeName switch
        {
            "chop_trees" => TryStartModeTool("axe", typeof(Axe), out error),
            "water_crops" => TryStartModeTool("watering_can", typeof(WateringCan), out error),
            "harvest_crops" => TryHarvestModeCrop(out error),
            "plant_crops" => TryPlantModeCrop(out error),
            "mine" => TryStartModeTool("pickaxe", typeof(Pickaxe), out error),
            "fish" => TryStartModeFishing(out error),
            _ => false
        };
        if (!success)
        {
            PauseMode("ModeActionFailed", _modeName, error?.Message ?? "the current mode action failed");
            return null;
        }

        if (_modeName == "fish")
        {
            _modeState = "fishing";
            _modePhaseTicks = 0;
        }
        else if (!IsToolMode())
        {
            _modeState = "verifying";
            _modePhaseTicks = 0;
        }
        else
        {
            _modeState = "tool_action";
            _modePhaseTicks = 0;
        }
        return null;
    }

    private ActionCompletion? TickModeVerification()
    {
        _modePhaseTicks++;
        if (_modePhaseTicks < ModeVerificationDelay)
            return null;

        if (IsModeTargetComplete())
        {
            _modeCompletedCount++;
            _modeState = "scanning";
            _modePhaseTicks = 0;
            _modeRetries = 0;
            return null;
        }

        if (_modeName == "chop_trees"
            && GetTreeAt(_modeTargetTile) is Tree tree
            && tree.health.Value < _modeBeforeHealth)
        {
            _modeRetries = 0;
            _modeState = "acting";
            _modePhaseTicks = 0;
            return null;
        }

        if (_modeRetries < ModeMaxRetries)
        {
            _modeRetries++;
            _modeState = "acting";
            _modePhaseTicks = 0;
            return null;
        }

        PauseMode("ModeActionFailed", _modeName ?? "work", "the target did not reach its expected state");
        return null;
    }

    private ActionCompletion? TickModeFishing()
    {
        var completion = TickFishingAction();
        if (completion is null)
            return null;
        if (completion.Status != "succeeded")
            return FinishMode("failed", completion.Error?.Code ?? "fishing_failed", completion.Error?.Message ?? "fishing failed");

        _modeCompletedCount++;
        _modeState = "scanning";
        _modePhaseTicks = 0;
        return null;
    }

    private bool HasModePrerequisites()
    {
        if (_shadow is null || _modeName is null)
            return false;
        switch (_modeName)
        {
            case "chop_trees":
                if (FindTool<Axe>() is null)
                {
                    PauseMode("MissingTool", "axe", "the companion has no axe");
                    return false;
                }
                break;
            case "water_crops":
                var wateringCan = FindTool<WateringCan>();
                if (wateringCan is null)
                {
                    PauseMode("MissingTool", "watering can", "the companion has no watering can");
                    return false;
                }
                if (wateringCan.WaterLeft <= 0)
                {
                    PauseMode("NoWater", "water crops", "the watering can is empty");
                    return false;
                }
                break;
            case "harvest_crops":
                if (!HasInventorySpace())
                {
                    PauseMode("InventoryFull", "harvest crops", "the companion inventory has no free slot");
                    return false;
                }
                break;
            case "plant_crops":
                if (FindSeedSlot() < 0)
                {
                    PauseMode("MissingSeed", "plant crops", "the companion has no seed");
                    return false;
                }
                break;
            case "mine":
                if (FindTool<Pickaxe>() is null)
                {
                    PauseMode("MissingTool", "pickaxe", "the companion has no pickaxe");
                    return false;
                }
                break;
            case "fish":
                if (FindTool<FishingRod>() is null)
                {
                    PauseMode("MissingTool", "fishing rod", "the companion has no fishing rod");
                    return false;
                }
                break;
        }
        if (_shadow.Stamina <= 0)
        {
            PauseMode("LowStamina", _modeName, "the companion has no stamina");
            return false;
        }
        return true;
    }

    private Point? FindModeTarget()
    {
        if (_visual?.currentLocation is null || _modeName is null)
            return null;
        var location = _visual.currentLocation;
        var origin = _visual.Tile;
        if (_modeName == "chop_trees")
        {
            var targets = location.terrainFeatures.Keys
                .Where(tile => location.terrainFeatures.TryGetValue(tile, out var feature)
                    && feature is Tree tree
                    && tree.growthStage.Value >= Tree.treeStage
                    && !tree.stump.Value
                    && tree.health.Value > 0)
                .OrderBy(tile => Vector2.DistanceSquared(tile, origin))
                .Select(tile => new Point((int)tile.X, (int)tile.Y))
                .ToList();
            return targets.Count == 0 ? null : targets[0];
        }
        if (_modeName is "water_crops" or "harvest_crops" or "plant_crops")
        {
            var targets = location.terrainFeatures.Keys
                .Where(tile => location.terrainFeatures.TryGetValue(tile, out var feature)
                    && feature is HoeDirt dirt
                    && _modeName switch
                {
                    "water_crops" => dirt.crop is not null && dirt.needsWatering(),
                    "harvest_crops" => dirt.crop is not null && dirt.readyForHarvest(),
                    "plant_crops" => dirt.crop is null,
                    _ => false
                })
                .OrderBy(tile => Vector2.DistanceSquared(tile, origin))
                .Select(tile => new Point((int)tile.X, (int)tile.Y))
                .ToList();
            return targets.Count == 0 ? null : targets[0];
        }
        if (_modeName == "mine")
        {
            var targets = location.objects.Keys
                .Where(tile => location.objects.TryGetValue(tile, out var obj) && obj.IsBreakableStone())
                .OrderBy(tile => Vector2.DistanceSquared(tile, origin))
                .Select(tile => new Point((int)tile.X, (int)tile.Y))
                .ToList();
            return targets.Count == 0 ? null : targets[0];
        }
        if (_modeName == "fish")
        {
            for (var radius = 1; radius <= 8; radius++)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    for (var dy = -radius; dy <= radius; dy++)
                    {
                        var tile = new Point((int)origin.X + dx, (int)origin.Y + dy);
                        if (location.isWaterTile(tile.X, tile.Y))
                            return tile;
                    }
                }
            }
        }
        return null;
    }

    private Point? FindModeApproachTile(GameLocation location, Point target)
    {
        var candidates = new[]
        {
            target,
            new Point(target.X + 1, target.Y),
            new Point(target.X - 1, target.Y),
            new Point(target.X, target.Y - 1),
            new Point(target.X, target.Y + 1)
        };
        foreach (var candidate in candidates)
        {
            if (location.isTilePassable(new xTile.Dimensions.Location(candidate.X, candidate.Y), Game1.viewport))
                return candidate;
        }
        return null;
    }

    private bool TryStartModeTool(string toolName, Type toolType, out ErrorDetail? error)
    {
        error = null;
        if (_visual?.currentLocation is null || _shadow is null || _modeRequestId is null)
        {
            error = new ErrorDetail { Code = "world_not_ready", Message = "the companion is not available" };
            return false;
        }

        if (_toolAction is not null)
        {
            error = new ErrorDetail { Code = "busy", Message = "another tool action is already active" };
            return false;
        }

        var tool = FindToolByType(toolType);
        if (tool is null)
        {
            error = new ErrorDetail { Code = "missing_tool", Message = $"the companion has no {toolName}" };
            return false;
        }

        if (_modeName == "chop_trees" && GetTreeAt(_modeTargetTile) is Tree treeBefore)
            _modeBeforeHealth = treeBefore.health.Value;

        var task = new ToolActionTask(
            _modeRequestId,
            toolName,
            toolType,
            _modeTargetTile,
            ReadTile(),
            _visual.currentLocation,
            ownedByMode: true);
        _toolAction = task;
        return true;
    }

    private bool IsToolMode()
    {
        return _modeName is "chop_trees" or "water_crops" or "mine";
    }

    private bool TryHarvestModeCrop(out ErrorDetail? error)
    {
        error = null;
        if (_shadow is null || _visual?.currentLocation is null
            || !_visual.currentLocation.terrainFeatures.TryGetValue(new Vector2(_modeTargetTile.X, _modeTargetTile.Y), out var feature)
            || feature is not HoeDirt dirt
            || dirt.crop is null
            || !dirt.readyForHarvest())
        {
            error = new ErrorDetail { Code = "harvest_target_missing", Message = "the crop is no longer ready for harvest" };
            return false;
        }
        if (!HasInventorySpace())
        {
            error = new ErrorDetail { Code = "inventory_full", Message = "the companion inventory has no free slot" };
            return false;
        }
        if (!dirt.crop.harvest(_modeTargetTile.X, _modeTargetTile.Y, dirt, null))
        {
            error = new ErrorDetail { Code = "harvest_failed", Message = "the crop could not be harvested" };
            return false;
        }
        return true;
    }

    private bool TryPlantModeCrop(out ErrorDetail? error)
    {
        error = null;
        if (_shadow is null || _visual?.currentLocation is null
            || !_visual.currentLocation.terrainFeatures.TryGetValue(new Vector2(_modeTargetTile.X, _modeTargetTile.Y), out var feature)
            || feature is not HoeDirt dirt
            || dirt.crop is not null)
        {
            error = new ErrorDetail { Code = "plant_target_invalid", Message = "the target is not an empty tilled tile" };
            return false;
        }
        var seedSlot = FindSeedSlot();
        if (seedSlot < 0 || _shadow.Items[seedSlot] is not StardewValley.Object seed)
        {
            error = new ErrorDetail { Code = "no_seed", Message = "the companion has no seed" };
            return false;
        }
        if (!dirt.plant(seed.ItemId, _shadow, false))
        {
            error = new ErrorDetail { Code = "plant_failed", Message = "the seed cannot be planted on the target tile" };
            return false;
        }
        seed.Stack--;
        if (seed.Stack <= 0)
            _shadow.Items[seedSlot] = null;
        return true;
    }

    private bool TryStartModeFishing(out ErrorDetail? error)
    {
        object? data = null;
        return TryCastFishingRod(_modeRequestId ?? "unknown", out data, out error);
    }

    private bool IsModeTargetComplete()
    {
        if (_visual?.currentLocation is null || _modeName is null)
            return false;
        var tile = new Vector2(_modeTargetTile.X, _modeTargetTile.Y);
        var location = _visual.currentLocation;
        return _modeName switch
        {
            "chop_trees" => !location.terrainFeatures.TryGetValue(tile, out var treeFeature)
                || treeFeature is not Tree tree
                || tree.stump.Value
                || tree.health.Value <= 0,
            "water_crops" => !location.terrainFeatures.TryGetValue(tile, out var waterFeature)
                || waterFeature is not HoeDirt waterDirt
                || waterDirt.crop is null
                || !waterDirt.needsWatering(),
            "harvest_crops" => !location.terrainFeatures.TryGetValue(tile, out var harvestFeature)
                || harvestFeature is not HoeDirt harvestDirt
                || harvestDirt.crop is null
                || !harvestDirt.readyForHarvest(),
            "plant_crops" => location.terrainFeatures.TryGetValue(tile, out var plantFeature)
                && plantFeature is HoeDirt plantDirt
                && plantDirt.crop is not null,
            "mine" => !location.objects.TryGetValue(tile, out var mineObject)
                || !mineObject.IsBreakableStone(),
            _ => false
        };
    }

    private Tree? GetTreeAt(Point tile)
    {
        return _visual?.currentLocation?.terrainFeatures.TryGetValue(new Vector2(tile.X, tile.Y), out var feature) == true
            ? feature as Tree
            : null;
    }

    private T? FindTool<T>() where T : Tool
    {
        return _shadow?.Items.FirstOrDefault(item => item is T) as T;
    }

    private Tool? FindToolByType(Type toolType)
    {
        return _shadow?.Items.FirstOrDefault(item => item is not null && toolType.IsInstanceOfType(item)) as Tool;
    }

    private int FindSeedSlot()
    {
        if (_shadow is null)
            return -1;
        for (var index = 0; index < _shadow.Items.Count; index++)
        {
            if (_shadow.Items[index] is StardewValley.Object item
                && item.Stack > 0
                && item.Category == StardewValley.Object.SeedsCategory)
                return index;
        }
        return -1;
    }

    private bool HasInventorySpace()
    {
        return _shadow?.Items.Any(item => item is null) == true;
    }

    private void PauseMode(string templateKey, string mode, string notice)
    {
        _modeState = "paused";
        _modePhaseTicks = 0;
        _modeLastNotice = notice;
        ShowModeBubble(templateKey, mode, notice);
    }

    private void ShowModeBubble(string templateKey, string mode, string target)
    {
        if (_visual is null || _modeBubbleCooldown > 0)
            return;
        var text = _bubbleTemplates.TryGetValue(templateKey, out var configured)
            ? configured
            : templateKey switch
            {
                "MissingTool" => "我没有{tool}，无法继续{mode}。",
                "MissingSeed" => "我没有可用的种子，无法继续播种。",
                "NoTilledSoil" => "没有找到可以播种的已开垦土地。",
                "InventoryFull" => "我的背包已满，无法继续工作。",
                "PathBlocked" => "我在{location}遇到了障碍，正在重新寻找路径。",
                "LowStamina" => "我太累了，需要休息。",
                "NoWater" => "我的浇水壶没水了，无法继续浇水。",
                "NoFishingWater" => "这里没有找到可以钓鱼的水域。",
                _ => "我无法完成当前动作。"
            };
        var tool = mode switch
        {
            "chop_trees" => "斧头",
            "water_crops" => "浇水壶",
            "mine" => "镐",
            "fish" => "鱼竿",
            _ => "工具"
        };
        text = text
            .Replace("{tool}", tool, StringComparison.Ordinal)
            .Replace("{mode}", mode, StringComparison.Ordinal)
            .Replace("{location}", _visual.currentLocation?.Name ?? "当前地点", StringComparison.Ordinal)
            .Replace("{target}", target, StringComparison.Ordinal);
        _visual.ShowTextAboveHead(text, 4000);
        _modeBubbleCooldown = ModeBubbleCooldownTicks;
    }

    private ActionCompletion FinishMode(string status, string? code, string? message)
    {
        var requestId = _modeRequestId ?? "unknown";
        var mode = _modeName ?? "unknown";
        var data = new
        {
            mode,
            state = status,
            completed_count = _modeCompletedCount,
            location = _visual?.currentLocation?.Name
        };
        var completion = new ActionCompletion
        {
            RequestId = requestId,
            Action = "start_mode",
            Status = status,
            Data = data,
            Error = code is null ? null : new ErrorDetail { Code = code, Message = message ?? code }
        };
        ClearModeState();
        return completion;
    }

    private void ClearModeState()
    {
        if (_visual is not null)
            _visual.controller = null;
        CancelToolActionInternal();
        ClearActionPresentation();
        _fishingActive = false;
        _fishingRequestId = null;
        _modeActive = false;
        _modeRequestId = null;
        _modeName = null;
        _modeState = "idle";
        _modeTargetTile = Point.Zero;
        _modePathTile = Point.Zero;
        _modePhaseTicks = 0;
        _modeRetries = 0;
        _modeCompletedCount = 0;
        _modeBlockedTicks = 0;
        _modeBeforeHealth = 0;
        _modeLastNotice = null;
        _modeBubbleCooldown = 0;
    }

    public bool TryStartMove(string requestId, string direction, int ticks, out MoveCompletion? failure)
    {
        failure = null;
        if (!TryReadDirection(direction, out var offset))
        {
            failure = MoveCompletion.Failed(requestId, "move_relative", direction, ticks, "invalid_direction", "direction must be up, down, left, or right");
            return false;
        }
        if (ticks is < 1 or > 30)
        {
            failure = MoveCompletion.Failed(requestId, "move_relative", direction, ticks, "invalid_ticks", "ticks must be between 1 and 30");
            return false;
        }
        if (!TryGetReady(out failure, requestId, "move_relative", direction, ticks))
            return false;

        var before = ReadTile();
        var distance = Math.Max(1, ticks / 5);
        var target = new Point(before.X + offset.X * distance, before.Y + offset.Y * distance);
        return TryStartPath(requestId, "move_relative", direction, ticks, target, before, out failure);
    }

    public bool TryStartMoveTo(string requestId, int x, int y, out MoveCompletion? failure)
    {
        failure = null;
        if (!TryGetReady(out failure, requestId, "move_to"))
            return false;

        var before = ReadTile();
        return TryStartPath(requestId, "move_to", null, 0, new Point(x, y), before, out failure);
    }

    public MoveCompletion? Cancel(string code, string message)
    {
        if (_activeMove is null || _visual is null)
            return null;

        var active = _activeMove;
        _activeMove = null;
        _visual.controller = null;
        return MoveCompletion.Failed(active.RequestId, active.Action, active.Direction, active.Ticks, code, message, "cancelled");
    }

    public ActionCompletion? TickToolAction()
    {
        if (_toolAction is null || _toolAction.OwnedByMode)
            return null;
        return AdvanceToolAction();
    }

    public ActionCompletion? CancelToolAction(string code, string message)
    {
        if (_toolAction is null || _toolAction.OwnedByMode)
            return null;

        var task = _toolAction;
        return CompleteToolAction(task, "cancelled", code, message);
    }

    public ActionCompletion? TickFishingAction()
    {
        if (!_fishingActive || _shadow is null || Game1.currentGameTime is null || _fishingRequestId is null)
            return null;

        var requestId = _fishingRequestId;
        var rod = _shadow.Items.FirstOrDefault(item => item is FishingRod) as FishingRod;
        if (rod is null)
        {
            _fishingActive = false;
            _fishingRequestId = null;
            return new ActionCompletion
            {
                RequestId = requestId,
                Action = "cast_fishing_rod",
                Status = "failed",
                Error = new ErrorDetail { Code = "no_fishing_rod", Message = "the companion has no fishing rod" }
            };
        }

        try
        {
            rod.tickUpdate(Game1.currentGameTime, _shadow);
            if (!rod.isNibbling || rod.isReeling || rod.hit || rod.pullingOutOfWater)
                return null;

            rod.DoFunction(_shadow.currentLocation, 1, 1, 1, _shadow);
            _fishingActive = false;
            _fishingRequestId = null;
            return new ActionCompletion
            {
                RequestId = requestId,
                Action = "cast_fishing_rod",
                Status = "succeeded",
                Data = new { cast = true, completed = true }
            };
        }
        catch (Exception exception)
        {
            _fishingActive = false;
            _fishingRequestId = null;
            return new ActionCompletion
            {
                RequestId = requestId,
                Action = "cast_fishing_rod",
                Status = "failed",
                Error = new ErrorDetail { Code = "fishing_failed", Message = exception.Message }
            };
        }
    }

    public ActionCompletion? CancelFishing(string code, string message)
    {
        if (!_fishingActive || _fishingRequestId is null)
            return null;

        var requestId = _fishingRequestId;
        ClearActionPresentation();
        _fishingActive = false;
        _fishingRequestId = null;
        return new ActionCompletion
        {
            RequestId = requestId,
            Action = "cast_fishing_rod",
            Status = "cancelled",
            Error = new ErrorDetail { Code = code, Message = message }
        };
    }

    public void ClearSpeechBubble()
    {
        _visual?.ClearTextAboveHead();
    }

    public bool TryFaceDirection(string direction, out ErrorDetail? error)
    {
        error = null;
        if (!TryGetReady(out var failure))
        {
            error = failure?.Error;
            return false;
        }
        if (!TryReadDirection(direction, out var offset))
        {
            error = new ErrorDetail { Code = "invalid_direction", Message = "direction must be up, down, left, or right" };
            return false;
        }

        var facing = offset switch
        {
            { X: 0, Y: -1 } => 0,
            { X: 1, Y: 0 } => 1,
            { X: 0, Y: 1 } => 2,
            _ => 3
        };
        SetFacingDirection(facing);
        SyncShadow();
        return true;
    }

    public bool TryStartUseTool(string requestId, string toolName, int x, int y, out object? data, out ErrorDetail? error)
    {
        data = null;
        error = null;
        if (!TryGetReady(out var failure))
        {
            error = failure?.Error;
            return false;
        }

        var normalizedTool = (toolName ?? "").Trim().ToLowerInvariant() switch
        {
            "wateringcan" => "watering_can",
            "weapon" => "sword",
            var value => value
        };
        var toolType = normalizedTool switch
        {
            "pickaxe" => typeof(Pickaxe),
            "axe" => typeof(Axe),
            "hoe" => typeof(Hoe),
            "watering_can" => typeof(WateringCan),
            "sword" => typeof(MeleeWeapon),
            _ => null
        };
        if (toolType is null)
        {
            error = new ErrorDetail { Code = "unknown_tool", Message = $"unknown tool: {toolName}" };
            return false;
        }

        if (_shadow is null || _visual?.currentLocation is null)
        {
            error = new ErrorDetail { Code = "world_not_ready", Message = "the companion is not spawned" };
            return false;
        }
        if (FindToolByType(toolType) is null)
        {
            error = new ErrorDetail { Code = "missing_tool", Message = $"the companion has no {normalizedTool}" };
            return false;
        }
        if (_shadow.Stamina <= 0)
        {
            error = new ErrorDetail { Code = "low_stamina", Message = "the companion has no stamina" };
            return false;
        }

        var task = new ToolActionTask(
            requestId,
            normalizedTool,
            toolType,
            new Point(x, y),
            ReadTile(),
            _visual.currentLocation,
            ownedByMode: false);
        _toolAction = task;
        data = new
        {
            tool = normalizedTool,
            target_tile = new TileDto { X = x, Y = y },
            phase = task.PhaseName,
            accepted = true
        };
        return true;
    }

    public bool TryInteract(int x, int y, out object? data, out ErrorDetail? error)
    {
        data = null;
        error = null;
        if (!TryGetReady(out var failure))
        {
            error = failure?.Error;
            return false;
        }

        var location = _visual!.currentLocation!;
        var tile = new Vector2(x, y);
        if (location.terrainFeatures.TryGetValue(tile, out var feature)
            && feature is HoeDirt dirt
            && dirt.crop is not null
            && dirt.readyForHarvest())
        {
            var harvested = dirt.crop.harvest(x, y, dirt, null);
            data = new { interaction = "harvest", tile = new TileDto { X = x, Y = y }, success = harvested };
            if (!harvested)
                error = new ErrorDetail { Code = "harvest_failed", Message = "the crop could not be harvested" };
            return harvested;
        }

        if (!location.objects.TryGetValue(tile, out var obj))
        {
            error = new ErrorDetail { Code = "nothing_to_interact", Message = $"nothing to interact with at ({x},{y})" };
            return false;
        }

        if (obj is Chest chest)
        {
            var items = chest.Items
                .Where(item => item is not null)
                .Select(item => new InventoryItemInfo
                {
                    Slot = -1,
                    Name = item!.Name ?? "",
                    DisplayName = item.DisplayName ?? item.Name ?? "",
                    QualifiedId = item.QualifiedItemId,
                    Stack = item.Stack,
                    Type = item is Tool ? "tool" : "item",
                    Edibility = item is StardewValley.Object food ? food.Edibility : -1
                })
                .ToList();
            data = new { interaction = "chest", tile = new TileDto { X = x, Y = y }, items };
            return true;
        }

        if (obj.Name is not null && (obj.Name.Contains("Ladder") || obj.Name.Contains("Shaft")))
        {
            if (location is MineShaft shaft)
            {
                var nextLevel = shaft.mineLevel + 1;
                var nextName = "UndergroundMine" + nextLevel;
                var nextLocation = Game1.getLocationFromName(nextName)
                    ?? MineShaft.GetMine(nextName);
                if (nextLocation is not null)
                {
                    var warped = TryWarp(nextLocation.Name, 6, 6, out _, out var warpError);
                    if (!warped)
                    {
                        error = warpError ?? new ErrorDetail { Code = "warp_failed", Message = "the Companion could not enter the next mine level" };
                        return false;
                    }
                    data = new
                    {
                        interaction = "ladder",
                        tile = new TileDto { X = x, Y = y },
                        destination = nextLocation.Name,
                        success = true
                    };
                    return true;
                }
            }

            error = new ErrorDetail { Code = "ladder_failed", Message = "the next mine level could not be loaded" };
            return false;
        }

        var success = obj.checkForAction(_shadow!);
        data = new { interaction = "object", object_name = obj.DisplayName ?? obj.Name, tile = new TileDto { X = x, Y = y }, success };
        if (!success)
            error = new ErrorDetail { Code = "interaction_failed", Message = $"could not interact with {obj.DisplayName ?? obj.Name}" };
        return success;
    }

    public bool TryWarp(string locationName, int x, int y, out object? data, out ErrorDetail? error)
    {
        data = null;
        error = null;
        if (!TryGetReady(out var failure))
        {
            error = failure?.Error;
            return false;
        }
        if (_activeMove is not null || _toolAction is not null)
        {
            error = new ErrorDetail { Code = "busy", Message = "the companion is currently busy" };
            return false;
        }

        var target = Game1.getLocationFromName(locationName);
        if (target is null)
        {
            error = new ErrorDetail { Code = "unknown_location", Message = $"location not found: {locationName}" };
            return false;
        }

        ClearActionPresentation();
        _visual!.currentLocation?.characters.Remove(_visual);
        _visual.controller = null;
        var position = new Vector2(x * Game1.tileSize, y * Game1.tileSize);
        _visual.Position = position;
        _visual.currentLocation = target;
        target.addCharacter(_visual);
        _shadow!.Position = position;
        _shadow.currentLocation = target;
        SyncShadow();
        data = new { location = target.Name, tile = new TileDto { X = x, Y = y } };
        _monitor.Log($"Warped {DisplayName} to {target.Name} ({x},{y}).", LogLevel.Info);
        return true;
    }

    public bool TryAttack(string requestId, out object? data, out ErrorDetail? error)
    {
        data = null;
        error = null;
        if (!TryGetReady(out var failure))
        {
            error = failure?.Error;
            return false;
        }

        var weapon = _shadow!.Items.FirstOrDefault(item => item is MeleeWeapon) as MeleeWeapon;
        var monster = FindNearestMonster();
        if (weapon is null || monster is null)
        {
            ClearActionPresentation();
            error = new ErrorDetail { Code = "no_attack_target", Message = weapon is null ? "the companion has no weapon" : "no monster in range" };
            data = new { attacked = false };
            return false;
        }

        _shadow.FaceToward(monster.Tile);
        try
        {
            var toolLocation = _shadow.GetToolLocation(true);
            weapon.DoDamage(_shadow.currentLocation, (int)toolLocation.X, (int)toolLocation.Y, _shadow.FacingDirection, 1, _shadow);
            StartToolPresentation(requestId, weapon, monster.Tile, CompanionNpc.MeleePresentation);
            data = new { attacked = true, monster = monster.Name, tile = new TileDto { X = (int)monster.Tile.X, Y = (int)monster.Tile.Y } };
            return true;
        }
        catch (Exception exception)
        {
            ClearActionPresentation();
            error = new ErrorDetail { Code = "attack_failed", Message = exception.Message };
            return false;
        }
    }

    public bool TryCastFishingRod(string requestId, out object? data, out ErrorDetail? error)
    {
        data = null;
        error = null;
        if (!TryGetReady(out var failure))
        {
            error = failure?.Error;
            return false;
        }

        var rod = _shadow!.Items.FirstOrDefault(item => item is FishingRod) as FishingRod;
        if (rod is null)
        {
            error = new ErrorDetail { Code = "no_fishing_rod", Message = "the companion has no fishing rod" };
            return false;
        }
        try
        {
            rod.beginUsing(_shadow.currentLocation, (int)_shadow.Position.X, (int)_shadow.Position.Y, _shadow);
            rod.castingPower = 1f;
            var offset = _shadow.FacingDirection switch
            {
                0 => new Point(0, -2),
                1 => new Point(2, 0),
                2 => new Point(0, 2),
                _ => new Point(-2, 0)
            };
            var castTile = new Vector2(_shadow.Tile.X + offset.X, _shadow.Tile.Y + offset.Y);
            StartToolPresentation(requestId, rod, castTile, CompanionNpc.CastPresentation);
            _fishingActive = true;
            _fishingRequestId = requestId;
            data = new { cast = true };
            return true;
        }
        catch (Exception exception)
        {
            ClearActionPresentation();
            error = new ErrorDetail { Code = "fishing_failed", Message = exception.Message };
            return false;
        }
    }

    public bool TrySetAutoCombat(bool enabled, out ErrorDetail? error)
    {
        error = null;
        if (!TryGetReady(out var failure))
        {
            error = failure?.Error;
            return false;
        }
        _autoCombat = enabled;
        return true;
    }

    public bool TryShowBubble(string text, int durationMs, out ErrorDetail? error)
    {
        error = null;
        EnsureSpawned();
        if (!IsSpawned || _visual is null || _shadow is null || _visual.currentLocation is null)
        {
            error = new ErrorDetail { Code = "world_not_ready", Message = "the companion is not spawned" };
            return false;
        }

        _visual.ShowTextAboveHead(text, durationMs);
        return true;
    }

    public bool TryEatItem(int? slot, out object? data, out ErrorDetail? error)
    {
        data = null;
        error = null;
        if (!TryGetReady(out var failure))
        {
            error = failure?.Error;
            return false;
        }

        StardewValley.Object? food = null;
        if (slot is >= 0 && slot < _shadow!.Items.Count && _shadow.Items[slot.Value] is StardewValley.Object selected && selected.Edibility > 0)
            food = selected;
        else
            food = _shadow!.Items.FirstOrDefault(item => item is StardewValley.Object candidate && candidate.Edibility > 0) as StardewValley.Object;

        if (food is null)
        {
            error = new ErrorDetail { Code = "no_edible_item", Message = "the companion has no edible item" };
            return false;
        }

        try
        {
            _shadow.eatObject(food);
            data = new { ate = true, item = food.DisplayName ?? food.Name };
            return true;
        }
        catch (Exception exception)
        {
            _shadow.Stamina = Math.Min(_shadow.MaxStamina, _shadow.Stamina + food.Edibility);
            _shadow.health = Math.Min(_shadow.maxHealth, _shadow.health + (int)(food.Edibility * 0.4f));
            food.Stack--;
            if (food.Stack <= 0)
                _shadow.Items[_shadow.Items.IndexOf(food)] = null;
            data = new { ate = true, item = food.DisplayName ?? food.Name, fallback = true };
            _monitor.Log($"Fallback eat implementation used: {exception.Message}", LogLevel.Debug);
            return true;
        }
    }

    public List<InventoryItemInfo> GetInventory()
    {
        if (_shadow is null)
            return new List<InventoryItemInfo>();
        return _shadow.Items
            .Select((item, index) => item is null ? null : new InventoryItemInfo
            {
                Slot = index,
                Name = item.Name ?? "",
                DisplayName = item.DisplayName ?? item.Name ?? "",
                QualifiedId = item.QualifiedItemId,
                Stack = item.Stack,
                Type = item is Tool ? "tool" : item is StardewValley.Object food && food.Edibility > 0 ? "food" : "item",
                Edibility = item is StardewValley.Object edible ? edible.Edibility : -1
            })
            .Where(item => item is not null)
            .Select(item => item!)
            .ToList();
    }

    public ObservationInfo? GetObservation(int radius)
    {
        var location = _visual?.currentLocation ?? Game1.player?.currentLocation;
        return location is null || _visual is null ? null : CompanionObservationScanner.Scan(location, _visual.Tile, radius);
    }

    public CompanionInfo GetInfo()
    {
        var inventory = GetInventory();
        return new CompanionInfo
        {
            Id = Id,
            DisplayName = DisplayName,
            Location = _visual?.currentLocation?.Name ?? "",
            Tile = _visual is null ? new TileDto() : ReadTile(),
            FacingDirection = FacingName(_visual?.FacingDirection ?? 2),
            Health = _shadow?.health ?? 0,
            MaxHealth = _shadow?.maxHealth ?? 0,
            Stamina = _shadow?.Stamina ?? 0,
            MaxStamina = _shadow?.MaxStamina ?? 0,
            InventoryCount = inventory.Count,
            Inventory = inventory,
            Mode = _modeActive ? _modeName! : _followActive ? "follow" : "direct",
            Status = _modeActive ? _modeState : _activeMove is not null ? "moving" : _toolAction?.PhaseName ?? (_fishingActive ? "fishing" : _followActive ? "following" : _autoCombat ? "auto-combat" : HasSpeechBubble ? "bubble" : "idle"),
            CurrentAction = CurrentAction,
            ActionPhase = _toolAction?.PhaseName,
            TargetTile = _toolAction is null ? null : new TileDto { X = _toolAction.TargetTile.X, Y = _toolAction.TargetTile.Y },
            ApproachTile = _toolAction?.ApproachTile is Point approach
                ? new TileDto { X = approach.X, Y = approach.Y }
                : null,
            Tool = _toolAction?.ToolName,
            ActionRequestId = _toolAction?.RequestId,
            WorldReady = Context.IsWorldReady && IsSpawned,
            Busy = IsBusy,
            AutoCombat = _autoCombat,
            Follow = GetFollowInfo(),
            ModeInfo = GetModeInfo(),
            Capabilities = new List<string>
            {
                "move_relative", "move_to", "face_direction", "use_tool", "interact", "warp_to",
                "observe", "get_inventory", "attack", "cast_fishing_rod", "set_auto_combat", "eat_item", "say", "bubble", "follow",
                "start_mode", "chop_trees", "water_crops", "harvest_crops", "plant_crops", "mine", "fish", "cancel"
            }
        };
    }

    public void SignalSleepReady()
    {
        _shadow?.SignalSleepReady();
    }

    public void WakeUp()
    {
        _shadow?.WakeUp();
        CancelToolActionInternal();
        ClearActionPresentation();
        _fishingActive = false;
        _fishingRequestId = null;
        ClearModeState();
        ClearFollowState();
    }

    public void Cleanup()
    {
        CancelMove();
        _visual?.ClearTextAboveHead();
        CancelToolActionInternal();
        ClearActionPresentation();
        if (_visual is not null)
            _visual.currentLocation?.characters.Remove(_visual);
        _visual = null;
        _shadow = null;
        _fishingActive = false;
        _fishingRequestId = null;
        _autoCombat = false;
        _autoCombatCooldown = 0;
        ClearModeState();
        ClearFollowState();
    }

    private bool TryGetReady(
        out MoveCompletion? failure,
        string requestId = "unknown",
        string action = "action",
        string? direction = null,
        int ticks = 0)
    {
        failure = null;
        EnsureSpawned();
        if (!IsSpawned || _visual is null || _shadow is null || _visual.currentLocation is null)
        {
            failure = MoveCompletion.Failed(requestId, action, direction, ticks, "world_not_ready", "the companion is not spawned");
            return false;
        }
        if (_activeMove is not null)
        {
            failure = MoveCompletion.Failed(requestId, action, direction, ticks, "busy", "the companion is already moving");
            return false;
        }
        if (_toolAction is not null)
        {
            failure = MoveCompletion.Failed(requestId, action, direction, ticks, "busy", "the companion is using a tool");
            return false;
        }
        return true;
    }

    private bool TryStartPath(string requestId, string action, string? direction, int ticks, Point target, TileDto before, out MoveCompletion? failure)
    {
        failure = null;
        if (_visual is null || _visual.currentLocation is null)
        {
            failure = MoveCompletion.Failed(requestId, action, direction, ticks, "world_not_ready", "the companion is not spawned");
            return false;
        }
        if (_activeMove is not null)
        {
            failure = MoveCompletion.Failed(requestId, action, direction, ticks, "busy", "the companion is already moving");
            return false;
        }
        try
        {
            _visual.controller = new PathFindController(_visual, _visual.currentLocation, target, 2);
            _activeMove = new ActiveMove(requestId, action, direction, ticks, before, target);
            return true;
        }
        catch (Exception error)
        {
            failure = MoveCompletion.Failed(requestId, action, direction, ticks, "pathfinding_failed", error.Message);
            return false;
        }
    }

    private bool TryReadDirection(string direction, out Point offset)
    {
        offset = (direction ?? "").ToLowerInvariant() switch
        {
            "up" => new Point(0, -1),
            "down" => new Point(0, 1),
            "left" => new Point(-1, 0),
            "right" => new Point(1, 0),
            _ => new Point(int.MinValue, int.MinValue)
        };
        return offset.X != int.MinValue;
    }

    private ActionCompletion? AdvanceToolAction()
    {
        if (_toolAction is null)
            return null;

        var task = _toolAction;
        if (!Context.IsWorldReady || _visual is null || _shadow is null || _visual.currentLocation is null)
            return CompleteToolAction(task, "failed", "world_not_ready", "the companion or world is unavailable");
        if (!ReferenceEquals(_visual.currentLocation, task.Location))
            return CompleteToolAction(task, "failed", "location_changed", "the companion changed location during the tool action");

        task.ElapsedTicks++;
        switch (task.Phase)
        {
            case ToolActionPhase.Validating:
                return AdvanceToolValidation(task);
            case ToolActionPhase.Locating:
                return AdvanceToolLocating(task);
            case ToolActionPhase.Moving:
                return AdvanceToolMovement(task);
            case ToolActionPhase.Facing:
                return AdvanceToolFacing(task);
            case ToolActionPhase.Executing:
                return AdvanceToolExecution(task);
            case ToolActionPhase.Verifying:
                return AdvanceToolVerification(task);
            default:
                return CompleteToolAction(task, "failed", "invalid_tool_phase", "the tool action entered an invalid phase");
        }
    }

    private ActionCompletion? AdvanceToolValidation(ToolActionTask task)
    {
        if (_shadow is null || _visual?.currentLocation is null)
            return CompleteToolAction(task, "failed", "world_not_ready", "the companion is not available");
        if (FindToolByType(task.ToolType) is null)
            return CompleteToolAction(task, "failed", "missing_tool", $"the companion has no {task.ToolName}");
        if (_shadow.Stamina <= 0)
            return CompleteToolAction(task, "failed", "low_stamina", "the companion has no stamina");
        if (!IsToolTargetValid(task))
            return CompleteToolAction(task, "failed", "invalid_tool_target", $"the target is not valid for {task.ToolName}");

        if (task.ToolType == typeof(Axe) && GetTreeAt(task.TargetTile) is Tree tree)
            task.BeforeTreeHealth = tree.health.Value;
        if (task.ToolType == typeof(Pickaxe)
            && _visual.currentLocation.objects.TryGetValue(new Vector2(task.TargetTile.X, task.TargetTile.Y), out var targetObject))
            task.BeforeBreakableStone = targetObject.IsBreakableStone();

        task.Phase = ToolActionPhase.Locating;
        task.PhaseTicks = 0;
        return null;
    }

    private ActionCompletion? AdvanceToolLocating(ToolActionTask task)
    {
        if (_visual is null || _visual.currentLocation is null)
            return CompleteToolAction(task, "failed", "world_not_ready", "the companion is not available");

        var current = ToPoint(ReadTile());
        if (!ToolTargetResolver.TryFindApproach(_visual.currentLocation, task.TargetTile, current, out var approach))
            return CompleteToolAction(task, "blocked", "no_approach_tile", "no reachable passable tile was found beside the target");

        task.ApproachTile = approach.Tile;
        task.FacingDirection = approach.FacingDirection;
        task.PhaseTicks = 0;
        if (current == approach.Tile)
        {
            task.Phase = ToolActionPhase.Facing;
            return null;
        }

        try
        {
            _visual.controller = new PathFindController(_visual, _visual.currentLocation, approach.Tile, 2);
            task.Phase = ToolActionPhase.Moving;
            return null;
        }
        catch (Exception exception)
        {
            _monitor.Log($"Tool target pathfinding failed: {exception.Message}", LogLevel.Debug);
            return CompleteToolAction(task, "blocked", "pathfinding_failed", exception.Message);
        }
    }

    private ActionCompletion? AdvanceToolMovement(ToolActionTask task)
    {
        if (_visual is null || task.ApproachTile is null)
            return CompleteToolAction(task, "failed", "world_not_ready", "the tool approach is unavailable");

        var current = ToPoint(ReadTile());
        if (current == task.ApproachTile.Value)
        {
            _visual.controller = null;
            task.Phase = ToolActionPhase.Facing;
            task.PhaseTicks = 0;
            return null;
        }

        task.PhaseTicks++;
        if (task.PhaseTicks > ToolPathTimeout)
            return CompleteToolAction(task, "blocked", "pathfinding_timeout", "the companion did not reach the approach tile");
        if (_visual.controller is null && task.PhaseTicks > 2)
            return CompleteToolAction(task, "blocked", "path_ended", "the companion path ended before reaching the approach tile");
        return null;
    }

    private ActionCompletion? AdvanceToolFacing(ToolActionTask task)
    {
        if (_visual is null || _shadow is null)
            return CompleteToolAction(task, "failed", "world_not_ready", "the companion is not available");

        SetFacingDirection(task.FacingDirection);
        SyncShadow();
        task.PhaseTicks++;
        if (task.PhaseTicks < 1)
            return null;

        task.Phase = ToolActionPhase.Executing;
        task.PhaseTicks = 0;
        return null;
    }

    private ActionCompletion? AdvanceToolExecution(ToolActionTask task)
    {
        if (_shadow is null || _visual?.currentLocation is null)
            return CompleteToolAction(task, "failed", "world_not_ready", "the companion is not available");

        var tool = FindToolByType(task.ToolType);
        if (tool is null)
            return CompleteToolAction(task, "failed", "missing_tool", $"the companion no longer has {task.ToolName}");
        if (_shadow.Stamina <= 0)
            return CompleteToolAction(task, "failed", "low_stamina", "the companion has no stamina");

        SetFacingDirection(task.FacingDirection);
        SyncShadow();
        var oldStamina = _shadow.Stamina;
        task.ApiCalled = true;
        try
        {
            var target = new Vector2(task.TargetTile.X, task.TargetTile.Y);
            var kind = tool is MeleeWeapon
                ? CompanionNpc.MeleePresentation
                : tool is WateringCan ? CompanionNpc.WaterPresentation : CompanionNpc.SwingPresentation;
            StartToolPresentation(task.RequestId, tool, target, kind);

            ToolExecutor.Execute(tool, _shadow.currentLocation, task.TargetTile, _shadow);

            _shadow.checkForExhaustion(oldStamina);
            task.Phase = ToolActionPhase.Verifying;
            task.PhaseTicks = 0;
            return null;
        }
        catch (Exception exception)
        {
            _monitor.Log($"Tool use failed: {exception.Message}", LogLevel.Debug);
            return CompleteToolAction(task, "failed", "tool_use_failed", exception.Message);
        }
    }

    private ActionCompletion? AdvanceToolVerification(ToolActionTask task)
    {
        task.PhaseTicks++;
        if (task.PhaseTicks < ToolVerificationDelay)
            return null;

        if (ToolVerifier.TryVerify(task, _visual!.currentLocation!, out var verification, out var contradicted))
            return CompleteToolAction(task, "succeeded", verification, null);
        if (contradicted)
            return CompleteToolAction(task, "failed", "verification_failed", verification);
        return CompleteToolAction(task, "completed", "api_returned", null);
    }

    private bool IsToolTargetValid(ToolActionTask task)
    {
        if (_visual?.currentLocation is null)
            return false;
        if (task.TargetTile.X < 0 || task.TargetTile.Y < 0)
            return false;
        if (task.ToolType == typeof(WateringCan))
        {
            return _visual.currentLocation.terrainFeatures.TryGetValue(
                new Vector2(task.TargetTile.X, task.TargetTile.Y),
                out var feature)
                && feature is HoeDirt;
        }
        return true;
    }

    private ActionCompletion CompleteToolAction(
        ToolActionTask task,
        string status,
        string verification,
        string? message)
    {
        task.Verification = verification;
        var after = ReadTile();
        var data = new
        {
            tool = task.ToolName,
            target_tile = new TileDto { X = task.TargetTile.X, Y = task.TargetTile.Y },
            approach_tile = task.ApproachTile is Point approach
                ? new TileDto { X = approach.X, Y = approach.Y }
                : null,
            before_tile = task.BeforeTile,
            after_tile = after,
            facing_direction = FacingName(task.FacingDirection),
            phase = task.PhaseName,
            api_called = task.ApiCalled,
            verification
        };
        if (status is not ("succeeded" or "completed"))
            ClearActionPresentation();
        ClearToolActionState();
        return new ActionCompletion
        {
            RequestId = task.RequestId,
            Action = "use_tool",
            Status = status,
            Data = data,
            Error = message is null ? null : new ErrorDetail { Code = verification, Message = message }
        };
    }

    private void ClearToolActionState()
    {
        if (_visual is not null)
            _visual.controller = null;
        _toolAction = null;
    }

    private void CancelToolActionInternal()
    {
        if (_toolAction is null)
            return;
        ClearActionPresentation();
        ClearToolActionState();
    }

    private void StartToolPresentation(string requestId, Tool tool, Vector2 targetTile, string kind)
    {
        if (_visual is null || _shadow is null)
            return;

        var facing = _shadow.FacingDirection;
        _visual.FacingDirection = facing;
        var target = new Point((int)targetTile.X, (int)targetTile.Y);
        var (texture, sourceRect) = GetToolIcon(tool);
        var ticks = kind switch
        {
            CompanionNpc.MeleePresentation => MeleePresentationTicks,
            CompanionNpc.WaterPresentation => WaterPresentationTicks,
            CompanionNpc.CastPresentation => CastPresentationTicks,
            _ => SwingPresentationTicks
        };
        _visual.ShowActionPresentation(requestId, kind, tool.Name, facing, target, ticks, texture, sourceRect);
        _monitor.Log(
            $"Action presentation start: request={requestId} kind={kind} tool={tool.Name} "
            + $"facing={facing} tile=({target.X},{target.Y}) ticks={ticks}.",
            LogLevel.Info);
    }

    private static (Texture2D? Texture, Rectangle SourceRect) GetToolIcon(Tool tool)
    {
        try
        {
            var sourceRect = Game1.getSourceRectForStandardTileSheet(Game1.toolSpriteSheet, tool.IndexOfMenuItemView, 16, 16);
            return (Game1.toolSpriteSheet, sourceRect);
        }
        catch (Exception)
        {
            return (null, Rectangle.Empty);
        }
    }

    private void TickActionPresentation()
    {
        if (_visual is null)
            return;
        var hadPresentation = _visual.HasActionPresentation;
        _visual.TickActionPresentation();
        if (hadPresentation && !_visual.HasActionPresentation)
            _monitor.Log("Action presentation ended.", LogLevel.Debug);
    }

    private void ClearActionPresentation()
    {
        if (_visual is null)
            return;
        if (_visual.HasActionPresentation)
            _monitor.Log("Action presentation cleared.", LogLevel.Debug);
        _visual.ClearActionPresentation();
    }

    private Monster? FindNearestMonster(float range = 256f)
    {
        if (_shadow?.currentLocation is null)
            return null;
        Monster? nearest = null;
        var nearestDistance = range;
        foreach (var character in _shadow.currentLocation.characters)
        {
            if (character is not Monster monster || monster.IsInvisible || monster.Health <= 0)
                continue;
            var distance = Vector2.Distance(_shadow.Position, monster.Position);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = monster;
            }
        }
        return nearest;
    }

    private void TickAutoCombat()
    {
        if (!_autoCombat || _autoCombatCooldown > 0)
        {
            if (_autoCombatCooldown > 0)
                _autoCombatCooldown--;
            return;
        }
        TryAttack("auto_combat", out _, out _);
        _autoCombatCooldown = 15;
    }

    private void CancelMove()
    {
        _activeMove = null;
        if (_visual is not null)
            _visual.controller = null;
    }

    private void SyncShadow()
    {
        if (_visual is null || _shadow is null)
            return;
        _shadow.Position = _visual.Position;
        _shadow.currentLocation = _visual.currentLocation;
        _shadow.FacingDirection = _visual.FacingDirection;
    }

    private void SetFacingDirection(int direction)
    {
        if (_visual is null)
            return;
        _visual.FacingDirection = direction;
        _visual.Sprite?.StopAnimation();
    }

    private TileDto ReadTile()
    {
        return new TileDto
        {
            X = (int)(_visual?.Tile.X ?? 0),
            Y = (int)(_visual?.Tile.Y ?? 0)
        };
    }

    private static Point ToPoint(TileDto tile)
    {
        return new Point(tile.X, tile.Y);
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

    private sealed class ActiveMove
    {
        public ActiveMove(string requestId, string action, string? direction, int ticks, TileDto before, Point target)
        {
            RequestId = requestId;
            Action = action;
            Direction = direction;
            Ticks = ticks;
            Before = before;
            Target = target;
        }

        public string RequestId { get; }
        public string Action { get; }
        public string? Direction { get; }
        public int Ticks { get; }
        public TileDto Before { get; }
        public Point Target { get; }
        public int ElapsedTicks { get; set; }
    }
}
