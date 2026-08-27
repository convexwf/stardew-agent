namespace StardewAgentMod;

internal sealed class ModConfig
{
    public string BridgeDirectory { get; set; } = "";
    public int LatestWriteIntervalSeconds { get; set; } = 5;
    public int SnapshotHistoryIntervalSeconds { get; set; } = 60;
    public int SnapshotHistoryLimit { get; set; } = 10;
    public Dictionary<string, string> BubbleTemplates { get; set; } = new()
    {
        ["MissingTool"] = "我没有{tool}，无法继续{mode}。",
        ["MissingSeed"] = "我没有可用的种子，无法继续播种。",
        ["NoTilledSoil"] = "没有找到可以播种的已开垦土地。",
        ["InventoryFull"] = "我的背包已满，无法继续工作。",
        ["PathBlocked"] = "我在{location}遇到了障碍，正在重新寻找路径。",
        ["LowStamina"] = "我太累了，需要休息。",
        ["NoWater"] = "我的浇水壶没水了，无法继续浇水。",
        ["NoFishingWater"] = "这里没有找到可以钓鱼的水域。",
        ["ModeActionFailed"] = "我无法完成{mode}的当前动作。"
    };
}
