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

    public void WriteJson<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Missing parent directory.");
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var json = JsonSerializer.Serialize(value, Protocol.JsonOptions);
            File.WriteAllText(tempPath, json, new UTF8Encoding(false));
            File.Move(tempPath, path);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
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
