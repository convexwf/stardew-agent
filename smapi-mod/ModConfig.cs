namespace StardewAgentMod;

internal sealed class ModConfig
{
    public string BridgeDirectory { get; set; } = "";
    public int LatestWriteIntervalSeconds { get; set; } = 5;
    public int SnapshotHistoryIntervalSeconds { get; set; } = 60;
    public int SnapshotHistoryLimit { get; set; } = 10;
}
