using System.Text.Json;
using Fleet.Platform.Storage.Models;
using Fleet.Ports.Releases;
using Fleet.Ports.Releases.Models;

namespace Fleet.Platform.Storage;

public sealed class JsonUpdateCheckCache : IUpdateCheckCache
{
    public CachedUpdateCheck? Load()
    {
        try
        {
            var file = JsonSerializer.Deserialize(
                File.ReadAllText(FleetPaths.UpdateCheckFile),
                FleetJsonContext.Default.UpdateCheckFile);

            return file is { Latest.Length: > 0 } ? new CachedUpdateCheck(file.Latest, file.CheckedAt, file.Repo) : null;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(CachedUpdateCheck check)
    {
        try
        {
            Directory.CreateDirectory(FleetPaths.Config);
            File.WriteAllText(
                FleetPaths.UpdateCheckFile,
                JsonSerializer.Serialize(
                    new UpdateCheckFile { Repo = check.Repo, Latest = check.Latest, CheckedAt = check.CheckedAt },
                    FleetJsonContext.Default.UpdateCheckFile));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
