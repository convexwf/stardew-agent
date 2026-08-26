namespace StardewAgentMod;

internal sealed class MoveExecutor
{
    private readonly CompanionController _companion;

    public MoveExecutor(CompanionController companion)
    {
        _companion = companion;
    }

    public bool IsBusy => _companion.IsBusy;

    public bool TryStartRelative(string requestId, string direction, int ticks, out MoveCompletion? immediateFailure)
    {
        return _companion.TryStartMove(requestId, direction, ticks, out immediateFailure);
    }

    public bool TryStartTo(string requestId, int x, int y, out MoveCompletion? immediateFailure)
    {
        return _companion.TryStartMoveTo(requestId, x, y, out immediateFailure);
    }

    public MoveCompletion? Tick()
    {
        return _companion.Tick();
    }

    public MoveCompletion? Cancel(string code, string message)
    {
        return _companion.Cancel(code, message);
    }
}

internal sealed class ActionCompletion
{
    public string RequestId { get; init; } = "";
    public string Action { get; init; } = "";
    public string Status { get; init; } = "failed";
    public object? Data { get; init; }
    public ErrorDetail? Error { get; init; }
}

internal sealed class MoveCompletion
{
    public string RequestId { get; init; } = "";
    public string? Direction { get; init; }
    public string Action { get; init; } = "move_relative";
    public int Ticks { get; init; }
    public string Status { get; init; } = "failed";
    public TileDto? BeforeTile { get; init; }
    public TileDto? AfterTile { get; init; }
    public TileDto? TargetTile { get; init; }
    public bool Moved { get; init; }
    public bool WorldReady { get; init; }
    public ErrorDetail? Error { get; init; }

    public static MoveCompletion Failed(
        string requestId,
        string action,
        string? direction,
        int ticks,
        string code,
        string message,
        string status = "failed")
    {
        return new MoveCompletion
        {
            RequestId = requestId,
            Action = action,
            Direction = direction,
            Ticks = ticks,
            Status = status,
            WorldReady = StardewModdingAPI.Context.IsWorldReady,
            Error = new ErrorDetail { Code = code, Message = message }
        };
    }
}
