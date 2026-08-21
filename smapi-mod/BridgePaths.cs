namespace StardewAgentMod;

internal sealed class BridgePaths
{
    public BridgePaths(string root)
    {
        Root = Path.GetFullPath(root);
        Pending = Path.Combine(Root, "actions", "pending");
        Processing = Path.Combine(Root, "actions", "processing");
        Archive = Path.Combine(Root, "actions", "archive");
        Results = Path.Combine(Root, "results");
        Snapshots = Path.Combine(Root, "snapshots");
        Errors = Path.Combine(Root, "errors");
    }

    public string Root { get; }
    public string Pending { get; }
    public string Processing { get; }
    public string Archive { get; }
    public string Results { get; }
    public string Snapshots { get; }
    public string Errors { get; }

    public void EnsureLayout()
    {
        foreach (var path in new[] { Pending, Processing, Archive, Results, Snapshots, Errors })
            Directory.CreateDirectory(path);
    }
}
