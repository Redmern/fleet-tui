using System.Text.Json;
using Fleet.Platform.Storage.Models;
using Fleet.Ports.Sessions;
using Fleet.Ports.Sessions.Models;
using Fleet.Shared;

namespace Fleet.Platform.Storage;

public sealed class JsonSessionStore : ISessionStore
{
    public IReadOnlyList<WindowSession> List()
    {
        if (!Directory.Exists(FleetPaths.WindowSessions))
        {
            return [];
        }

        var sessions = new List<WindowSession>();

        foreach (var file in Directory.EnumerateFiles(FleetPaths.WindowSessions, "*.json"))
        {
            var loaded = BusyFiles.Retry(() => File.ReadAllText(file), TimeSpan.FromMilliseconds(200));

            try
            {
                if (loaded is not null
                    && JsonSerializer.Deserialize(loaded, FleetJsonContext.Default.WindowSessionFile) is { Name.Length: > 0 } stored)
                {
                    sessions.Add(new WindowSession(
                        stored.Name,
                        [.. stored.Projects.Where(p => p.Name.Length > 0).Select(ToProject)],
                        stored.Showing is { Name.Length: > 0 } showing ? ToProject(showing) : null));
                }
            }
            catch (JsonException)
            {
            }
        }

        return [.. sessions.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)];
    }

    public void Save(WindowSession session)
    {
        if (FileFor(session.Name) is not { } file)
        {
            return;
        }

        var json = JsonSerializer.Serialize(
            new WindowSessionFile
            {
                Name = session.Name,
                Projects = [.. session.Projects.Select(ToFile)],
                Showing = session.Showing is { } showing ? ToFile(showing) : null,
            },
            FleetJsonContext.Default.WindowSessionFile);

        BusyFiles.Replace(file, temp => File.WriteAllText(temp, json));
    }

    public bool Remove(string name)
    {
        if (FileFor(name) is not { } file || !File.Exists(file))
        {
            return false;
        }

        File.Delete(file);
        return true;
    }

    private static SessionProject ToProject(WindowSessionProject stored) =>
        new(stored.Name, string.IsNullOrWhiteSpace(stored.Host) ? null : stored.Host);

    private static WindowSessionProject ToFile(SessionProject project) =>
        new() { Name = project.Name, Host = project.Host };

    private static string? FileFor(string session)
    {
        var name = ProjectName.Sanitize(session);

        return name.Length == 0 ? null : Path.Combine(FleetPaths.WindowSessions, name + ".json");
    }
}