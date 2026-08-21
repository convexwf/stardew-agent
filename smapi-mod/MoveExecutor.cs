using StardewModdingAPI;
using StardewValley;

namespace StardewAgentMod;

internal sealed class MoveExecutor
{
    private readonly IModHelper _helper;
    private ActiveMove? _active;

    public MoveExecutor(IModHelper helper)
    {
        _helper = helper;
    }

    public bool IsBusy => _active is not null;

    public bool TryStart(string requestId, string direction, int ticks, out MoveCompletion? immediateFailure)
    {
        immediateFailure = null;
        if (_active is not null)
        {
            immediateFailure = MoveCompletion.Failed(requestId, direction, ticks, "busy", "another move is running");
            return false;
        }

        if (!Context.IsWorldReady || Game1.player is null || Game1.currentLocation is null)
        {
            immediateFailure = MoveCompletion.Failed(requestId, direction, ticks, "world_not_ready", "the game world is not ready");
            return false;
        }

        if (ticks is < 1 or > 30)
        {
            immediateFailure = MoveCompletion.Failed(requestId, direction, ticks, "invalid_ticks", "ticks must be between 1 and 30");
            return false;
        }

        if (GetMoveButton(direction) is null)
        {
            immediateFailure = MoveCompletion.Failed(requestId, direction, ticks, "invalid_direction", "direction must be up, down, left, or right");
            return false;
        }

        _active = new ActiveMove
        {
            RequestId = requestId,
            Direction = direction,
            Ticks = ticks,
            RemainingTicks = ticks,
            BeforeTile = ReadPlayerTile()
        };
        return true;
    }

    public MoveCompletion? Tick()
    {
        if (_active is null)
            return null;

        var active = _active;
        var button = GetMoveButton(active.Direction);
        if (button is null || Game1.player is null)
        {
            _active = null;
            return MoveCompletion.Failed(active.RequestId, active.Direction, active.Ticks, "input_unavailable", "player input is unavailable");
        }

        _helper.Input.Press(button.Value);
        active.RemainingTicks--;
        if (active.RemainingTicks > 0)
            return null;

        var afterTile = ReadPlayerTile();
        _active = null;
        return new MoveCompletion
        {
            RequestId = active.RequestId,
            Direction = active.Direction,
            Ticks = active.Ticks,
            Status = active.BeforeTile.X == afterTile.X && active.BeforeTile.Y == afterTile.Y ? "blocked" : "succeeded",
            BeforeTile = active.BeforeTile,
            AfterTile = afterTile,
            Moved = active.BeforeTile.X != afterTile.X || active.BeforeTile.Y != afterTile.Y,
            WorldReady = Context.IsWorldReady
        };
    }

    public MoveCompletion? Cancel(string code, string message)
    {
        if (_active is null)
            return null;

        var active = _active;
        _active = null;
        return MoveCompletion.Failed(active.RequestId, active.Direction, active.Ticks, code, message);
    }

    private static TileDto ReadPlayerTile()
    {
        return new TileDto
        {
            X = (int)Game1.player.Tile.X,
            Y = (int)Game1.player.Tile.Y
        };
    }

    private static SButton? GetMoveButton(string direction)
    {
        var options = Game1.options;
        return direction.ToLowerInvariant() switch
        {
            "up" => options.moveUpButton.Length > 0 ? options.moveUpButton[0].ToSButton() : SButton.W,
            "down" => options.moveDownButton.Length > 0 ? options.moveDownButton[0].ToSButton() : SButton.S,
            "left" => options.moveLeftButton.Length > 0 ? options.moveLeftButton[0].ToSButton() : SButton.A,
            "right" => options.moveRightButton.Length > 0 ? options.moveRightButton[0].ToSButton() : SButton.D,
            _ => null
        };
    }

    private sealed class ActiveMove
    {
        public string RequestId { get; init; } = "";
        public string Direction { get; init; } = "";
        public int Ticks { get; init; }
        public int RemainingTicks { get; set; }
        public TileDto BeforeTile { get; init; } = new();
    }
}

internal sealed class MoveCompletion
{
    public string RequestId { get; init; } = "";
    public string Direction { get; init; } = "";
    public int Ticks { get; init; }
    public string Status { get; init; } = "failed";
    public TileDto? BeforeTile { get; init; }
    public TileDto? AfterTile { get; init; }
    public bool Moved { get; init; }
    public bool WorldReady { get; init; }
    public ErrorDetail? Error { get; init; }

    public static MoveCompletion Failed(string requestId, string direction, int ticks, string code, string message)
    {
        return new MoveCompletion
        {
            RequestId = requestId,
            Direction = direction,
            Ticks = ticks,
            Status = "failed",
            WorldReady = Context.IsWorldReady,
            Error = new ErrorDetail { Code = code, Message = message }
        };
    }
}
