using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Pathfinding;

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

    public CompanionController(IModHelper helper, IMonitor monitor)
    {
        _helper = helper;
        _monitor = monitor;
    }

    public bool IsSpawned => _visual is not null && _shadow is not null;

    public bool IsBusy => _activeMove is not null;

    public void EnsureSpawned()
    {
        if (IsSpawned || !Context.IsWorldReady || Game1.currentLocation is null || Game1.player is null)
            return;

        var location = Game1.currentLocation;
        var position = Game1.player.Position + new Vector2(Game1.tileSize, 0f);
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
        _monitor.Log($"Spawned {DisplayName} in {location.Name}.", LogLevel.Info);
    }

    public void EnsureSameLocationAsPlayer()
    {
        if (!IsSpawned || Game1.currentLocation is null || Game1.player is null || _visual is null)
            return;

        if (_visual.currentLocation?.Name == Game1.currentLocation.Name)
            return;

        CancelMove();
        MoveToLocation(Game1.currentLocation, Game1.player.Position + new Vector2(Game1.tileSize, 0f));
        _monitor.Log($"Moved {DisplayName} to {Game1.currentLocation.Name} with the player.", LogLevel.Debug);
    }

    public bool TryStartMove(string requestId, string direction, int ticks, out MoveCompletion? failure)
    {
        failure = null;
        EnsureSpawned();
        EnsureSameLocationAsPlayer();

        if (!IsSpawned || _visual is null || Game1.currentLocation is null)
        {
            failure = MoveCompletion.Failed(requestId, direction, ticks, "world_not_ready", "the companion is not spawned");
            return false;
        }

        if (_activeMove is not null)
        {
            failure = MoveCompletion.Failed(requestId, direction, ticks, "busy", "the companion is already moving");
            return false;
        }

        if (ticks is < 1 or > 30)
        {
            failure = MoveCompletion.Failed(requestId, direction, ticks, "invalid_ticks", "ticks must be between 1 and 30");
            return false;
        }

        var offset = direction.ToLowerInvariant() switch
        {
            "up" => new Point(0, -1),
            "down" => new Point(0, 1),
            "left" => new Point(-1, 0),
            "right" => new Point(1, 0),
            _ => (Point?)null
        };
        if (offset is null)
        {
            failure = MoveCompletion.Failed(requestId, direction, ticks, "invalid_direction", "direction must be up, down, left, or right");
            return false;
        }

        var before = ReadTile();
        var distance = Math.Max(1, ticks / 5);
        var target = new Point(before.X + offset.Value.X * distance, before.Y + offset.Value.Y * distance);
        try
        {
            _visual.controller = new PathFindController(_visual, Game1.currentLocation, target, 2);
            _activeMove = new ActiveMove(requestId, direction, ticks, before, target);
            return true;
        }
        catch (Exception error)
        {
            failure = MoveCompletion.Failed(requestId, direction, ticks, "pathfinding_failed", error.Message);
            return false;
        }
    }

    public MoveCompletion? Tick()
    {
        if (_activeMove is null || _visual is null)
            return null;

        SyncShadow();
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
            Direction = active.Direction,
            Ticks = active.Ticks,
            Status = moved ? "succeeded" : "blocked",
            BeforeTile = active.Before,
            AfterTile = after,
            Moved = moved,
            WorldReady = Context.IsWorldReady,
            Error = timedOut && !moved
                ? new ErrorDetail { Code = "movement_timeout", Message = "the companion did not reach a passable tile" }
                : null
        };
    }

    public MoveCompletion? Cancel(string code, string message)
    {
        if (_activeMove is null || _visual is null)
            return null;

        var active = _activeMove;
        _activeMove = null;
        _visual.controller = null;
        return MoveCompletion.Failed(active.RequestId, active.Direction, active.Ticks, code, message);
    }

    public CompanionInfo GetInfo()
    {
        return new CompanionInfo
        {
            Id = Id,
            DisplayName = DisplayName,
            Location = _visual?.currentLocation?.Name ?? "",
            Tile = _visual is null ? new TileDto() : ReadTile(),
            WorldReady = Context.IsWorldReady && IsSpawned,
            Busy = IsBusy
        };
    }

    public void SignalSleepReady()
    {
        _shadow?.SignalSleepReady();
    }

    public void WakeUp()
    {
        _shadow?.WakeUp();
    }

    public void Cleanup()
    {
        CancelMove();
        if (_visual is not null)
            _visual.currentLocation?.characters.Remove(_visual);
        _visual = null;
        _shadow = null;
    }

    private void CancelMove()
    {
        _activeMove = null;
        if (_visual is not null)
            _visual.controller = null;
    }

    private void MoveToLocation(GameLocation location, Vector2 position)
    {
        if (_visual is null || _shadow is null)
            return;

        _visual.currentLocation?.characters.Remove(_visual);
        _visual.controller = null;
        _visual.Position = position;
        _visual.currentLocation = location;
        location.addCharacter(_visual);
        _shadow.Position = position;
        _shadow.currentLocation = location;
        _shadow.FacingDirection = _visual.FacingDirection;
    }

    private void SyncShadow()
    {
        if (_visual is null || _shadow is null)
            return;

        _shadow.Position = _visual.Position;
        _shadow.currentLocation = _visual.currentLocation;
        _shadow.FacingDirection = _visual.FacingDirection;
    }

    private TileDto ReadTile()
    {
        return new TileDto
        {
            X = (int)(_visual?.Tile.X ?? 0),
            Y = (int)(_visual?.Tile.Y ?? 0)
        };
    }

    private sealed class ActiveMove
    {
        public ActiveMove(string requestId, string direction, int ticks, TileDto before, Point target)
        {
            RequestId = requestId;
            Direction = direction;
            Ticks = ticks;
            Before = before;
            Target = target;
        }

        public string RequestId { get; }
        public string Direction { get; }
        public int Ticks { get; }
        public TileDto Before { get; }
        public Point Target { get; }
        public int ElapsedTicks { get; set; }
    }
}
