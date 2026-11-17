using System.Collections.Concurrent;
using System.Text.Json;
using Fleet.Ports.Forge;
using Fleet.Ports.Forge.Enums;

namespace Fleet.Platform.Forge;

public sealed class CachedForgeProbe(IForgeProbe inner, Func<string> file) : IForgeProbe
{
    public const string ForgeWord = "forgejo-or-gitea";

    public const string NoneWord = "none";

    private readonly ConcurrentDictionary<string, ForgeProbeResult> _seen = new(StringComparer.OrdinalIgnoreCase);

    public async Task<ForgeProbeResult> ProbeAsync(string host, CancellationToken ct = default)
    {
        var key = host.Trim().ToLowerInvariant();

        if (_seen.TryGetValue(key, out var seen))
        {
            return seen;
        }

        if (Stored(key) is { } stored)
        {
            _seen[key] = stored;

            return stored;
        }

        var probed = await inner.ProbeAsync(key, ct).ConfigureAwait(false);

        if (probed != ForgeProbeResult.Unreachable)
        {
            _seen[key] = probed;
            Store(key, probed);
        }

        return probed;
    }

    private ForgeProbeResult? Stored(string key) =>
        Load().Hosts.TryGetValue(key, out var word)
            ? word switch
            {
                ForgeWord => ForgeProbeResult.ForgejoOrGitea,
                NoneWord => ForgeProbeResult.NotAForge,
                _ => null,
            }
            : null;

    private ForgeCacheFile Load()
    {
        try
        {
            var path = file();

            var stored = File.Exists(path)
                ? JsonSerializer.Deserialize(File.ReadAllText(path), ForgeJsonContext.Default.ForgeCacheFile)
                : null;

            return stored is { Hosts: not null } ? stored : new ForgeCacheFile();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new ForgeCacheFile();
        }
    }

    private void Store(string key, ForgeProbeResult result)
    {
        try
        {
            var path = file();
            var stored = Load();

            stored.Hosts[key] = result == ForgeProbeResult.ForgejoOrGitea ? ForgeWord : NoneWord;

            if (Path.GetDirectoryName(path) is { Length: > 0 } folder)
            {
                Directory.CreateDirectory(folder);
            }

            var temporary = $"{path}.{Environment.ProcessId}.tmp";

            File.WriteAllText(temporary, JsonSerializer.Serialize(stored, ForgeJsonContext.Default.ForgeCacheFile));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
