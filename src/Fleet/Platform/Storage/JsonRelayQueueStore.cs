using System.Text.Json;
using Fleet.Platform.Storage.Models;
using Fleet.Ports.Relay;
using Fleet.Ports.Relay.Models;

namespace Fleet.Platform.Storage;

public sealed class JsonRelayQueueStore(string? folder = null) : IRelayQueueStore
{
    private readonly string _folder = folder ?? FleetPaths.RelayQueue;

    public IReadOnlyList<string> Projects()
    {
        try
        {
            return Directory.Exists(_folder)
                ? [.. Directory.EnumerateFiles(_folder, "*.json")
                    .Select(TryRead)
                    .Where(f => f is { Prompts.Count: > 0 } && f.Project.Length > 0)
                    .Select(f => f!.Project)]
                : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public IReadOnlyList<QueuedPrompt> Load(string project) =>
        Read(FileFor(project)) is { } file && string.Equals(file.Project, project, StringComparison.OrdinalIgnoreCase)
            ? [.. file.Prompts.Select(p => new QueuedPrompt(p.Text, p.QueuedAt))]
            : [];

    public void Save(string project, IReadOnlyList<QueuedPrompt> prompts)
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

    private static RelayQueueFile? TryRead(string path)
    {
        try
        {
            return Read(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private string FileFor(string project)
    {
        var name = string.Concat(project.ToLowerInvariant().Split(Path.GetInvalidFileNameChars())).Trim();

        return Path.Combine(_folder, $"queue-{name}.json");
    }

    private static RelayQueueFile? Read(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(path), FleetJsonContext.Default.RelayQueueFile);
        }
        catch (JsonException)
        {
            File.Move(path, $"{path}.{DateTime.UtcNow.Ticks}.corrupt");
            return null;
        }
    }
}
