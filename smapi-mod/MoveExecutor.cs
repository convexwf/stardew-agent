namespace StardewAgentMod;

internal sealed class MoveExecutor
{
    private readonly CompanionController _companion;

    public MoveExecutor(CompanionController companion)
    {
        _companion = companion;
    }

    public bool IsBusy => _companion.IsBusy;

    public bool TryStart(string requestId, string direction, int ticks, out MoveCompletion? immediateFailure)
    {
        return _companion.TryStartMove(requestId, direction, ticks, out immediateFailure);
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
            WorldReady = StardewModdingAPI.Context.IsWorldReady,
            Error = new ErrorDetail { Code = code, Message = message }
        };
    }
}
