using System.Text;
using System.Text.Json;

namespace StardewAgentMod;

internal sealed class BridgeFileStore
{
    private readonly BridgePaths _paths;

    public BridgeFileStore(BridgePaths paths)
    {
        _paths = paths;
    }

    public BridgePaths Paths => _paths;

    public long GetLatestWriteSequence()
    {
        var path = Path.Combine(_paths.Snapshots, "snapshot-latest.json");
        if (!File.Exists(path))
            return 0;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement
                .GetProperty("payload")
                .GetProperty("latest_write_sequence")
                .GetInt64();
        }
        catch (Exception)
        {
            return 0;
        }
    }

    public int? GetLatestSnapshotIndex()
    {
        var path = Path.Combine(_paths.Snapshots, "snapshot-latest.json");
        if (!File.Exists(path))
            return null;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement
                .GetProperty("payload")
                .GetProperty("snapshot_index")
                .GetInt32();
        }
        catch (Exception)
        {
            return null;
        }
    }

    public (long Sequence, int Index) GetLatestHistory()
    {
        var latest = (Sequence: 0L, Index: -1);
        foreach (var path in Directory.EnumerateFiles(_paths.Snapshots, "snapshot-*.json"))
        {
            if (Path.GetFileName(path) == "snapshot-latest.json")
                continue;

            var index = ParseSnapshotIndex(path);
            if (index is null)
                continue;

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                var payload = document.RootElement.GetProperty("payload");
                var sequence = payload.GetProperty("snapshot_sequence").GetInt64();
                if (sequence > latest.Sequence)
                    latest = (sequence, index.Value);
            }
            catch (Exception)
            {
                // Ignore incomplete or legacy history files during startup.
            }
        }

        return latest;
    }

    public void NormalizeSnapshotSlots(int maxHistory)
    {
        foreach (var path in Directory.EnumerateFiles(_paths.Snapshots, "snapshot-*.json"))
        {
            if (Path.GetFileName(path) == "snapshot-latest.json")
                continue;

            var index = ParseSnapshotIndex(path);
            if (index is null || index.Value < 0 || index.Value >= maxHistory)
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                    // The next snapshot write can retry cleanup after readers release it.
                }
            }
        }
    }

    public void WriteJson<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Missing parent directory.");
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var json = JsonSerializer.Serialize(value, Protocol.JsonOptions);
            File.WriteAllText(tempPath, json, new UTF8Encoding(false));
            File.Move(tempPath, path, true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    public void ReplaceJson<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Missing parent directory.");
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var json = JsonSerializer.Serialize(value, Protocol.JsonOptions);
            File.WriteAllText(tempPath, json, new UTF8Encoding(false));
            if (File.Exists(path))
                File.Move(tempPath, path, true);
            else
                File.Move(tempPath, path);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private static int? ParseSnapshotIndex(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return int.TryParse(name["snapshot-".Length..], out var index) ? index : null;
    }

    public string? TryClaim(string pendingPath)
    {
        var fileName = Path.GetFileName(pendingPath);
        var processingPath = Path.Combine(_paths.Processing, fileName);
        try
        {
            File.Move(pendingPath, processingPath);
            return processingPath;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Archive(string processingPath)
    {
        var target = Path.Combine(_paths.Archive, Path.GetFileName(processingPath));
        if (File.Exists(target))
            File.Delete(target);
        File.Move(processingPath, target);
    }

    public void MoveToErrors(string processingPath)
    {
        var target = Path.Combine(_paths.Errors, $"{Path.GetFileNameWithoutExtension(processingPath)}-{Guid.NewGuid():N}.json");
        File.Move(processingPath, target);
    }
}
