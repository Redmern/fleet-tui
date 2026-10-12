using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fleet.Platform.Storage.Models;
using Fleet.Ports;
using Fleet.Ports.Relay;
using Fleet.Ports.Relay.Models;

namespace Fleet.Platform.Storage;

public sealed class JsonRelayQueueStore(string? folder = null, IFleetLog? log = null) : IRelayQueueStore
{
    private readonly string _folder = folder ?? FleetPaths.RelayQueue;

    private readonly Lock _lock = new();

    private readonly Dictionary<string, IReadOnlyList<QueuedPrompt>> _known = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> Projects()
    {
        var onDisk = new List<string>();

        try
        {
            if (Directory.Exists(_folder))
            {
                onDisk.AddRange(Directory.EnumerateFiles(_folder, "queue-*.json")
                    .Select(TryRead)
                    .Where(f => f is { Prompts.Count: > 0 } && f.Project.Length > 0)
                    .Select(f => f!.Project));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log?.Swallowed(e);
        }

        lock (_lock)
        {
            return [.. onDisk
                .Where(p => !_known.TryGetValue(p, out var known) || known.Count > 0)
                .Concat(_known.Where(k => k.Value.Count > 0).Select(k => k.Key))
                .Distinct(StringComparer.OrdinalIgnoreCase)];
        }
    }

    public IReadOnlyList<QueuedPrompt> Load(string project)
    {
        lock (_lock)
        {
            if (_known.TryGetValue(project, out var known))
            {
                return known;
            }
        }

        var read = TryRead(FileFor(project)) is { } file && string.Equals(file.Project, project, StringComparison.OrdinalIgnoreCase)
            ? [.. file.Prompts.Select(p => new QueuedPrompt(p.Text, p.QueuedAt))]
            : Array.Empty<QueuedPrompt>();

        lock (_lock)
        {
            return _known.TryAdd(project, read) ? read : _known[project];
        }
    }

    public void Save(string project, IReadOnlyList<QueuedPrompt> prompts)
    {
        lock (_lock)
        {
            _known[project] = [.. prompts];
        }

        try
        {
            Write(project, prompts);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log?.Swallowed(e);
        }
    }

    public string FileFor(string project)
    {
        var key = project.ToLowerInvariant();
        var readable = string.Concat(key.Split(Path.GetInvalidFileNameChars())).Trim();
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];

        return Path.Combine(_folder, $"queue-{readable}-{hash}.json");
    }

    private void Write(string project, IReadOnlyList<QueuedPrompt> prompts)
    {
        var path = FileFor(project);

        if (prompts.Count == 0)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return;
        }

        Directory.CreateDirectory(_folder);

        var file = new RelayQueueFile
        {
            Project = project,
            Prompts = [.. prompts.Select(p => new RelayQueueEntry { Text = p.Text, QueuedAt = p.QueuedAt })],
        };
        var temporary = path + ".tmp";

        File.WriteAllText(temporary, JsonSerializer.Serialize(file, FleetJsonContext.Default.RelayQueueFile));
        File.Move(temporary, path, overwrite: true);
    }

    private RelayQueueFile? TryRead(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize(File.ReadAllText(path), FleetJsonContext.Default.RelayQueueFile)
                : null;
        }
        catch (JsonException e)
        {
            log?.Swallowed(e);
            KeepAside(path);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log?.Swallowed(e);
            return null;
        }
    }

    private void KeepAside(string path)
    {
        try
        {
            File.Move(path, $"{path}.{DateTime.UtcNow.Ticks}.corrupt");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log?.Swallowed(e);
        }
    }
}
