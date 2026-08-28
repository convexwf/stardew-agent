using Microsoft.Xna.Framework;
using StardewValley;

namespace StardewAgentMod;

internal enum ToolActionPhase
{
    Validating,
    Locating,
    Moving,
    Facing,
    Executing,
    Verifying
}

internal sealed class ToolActionTask
{
    public ToolActionTask(
        string requestId,
        string toolName,
        Type toolType,
        Point targetTile,
        TileDto beforeTile,
        GameLocation location,
        bool ownedByMode)
    {
        RequestId = requestId;
        ToolName = toolName;
        ToolType = toolType;
        TargetTile = targetTile;
        BeforeTile = beforeTile;
        Location = location;
        OwnedByMode = ownedByMode;
    }

    public string RequestId { get; }
    public string ToolName { get; }
    public Type ToolType { get; }
    public Point TargetTile { get; }
    public TileDto BeforeTile { get; }
    public GameLocation Location { get; }
    public bool OwnedByMode { get; }
    public ToolActionPhase Phase { get; set; } = ToolActionPhase.Validating;
    public Point? ApproachTile { get; set; }
    public int FacingDirection { get; set; } = 2;
    public int ElapsedTicks { get; set; }
    public int PhaseTicks { get; set; }
    public bool ApiCalled { get; set; }
    public string Verification { get; set; } = "pending";
    public float? BeforeTreeHealth { get; set; }
    public bool BeforeBreakableStone { get; set; }

    public string PhaseName => Phase switch
    {
        ToolActionPhase.Validating => "validating",
        ToolActionPhase.Locating => "locating",
        ToolActionPhase.Moving => "moving",
        ToolActionPhase.Facing => "facing",
        ToolActionPhase.Executing => "executing",
        ToolActionPhase.Verifying => "verifying",
        _ => "unknown"
    };
}
