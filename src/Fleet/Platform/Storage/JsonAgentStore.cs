using System.Text.Json;
using Fleet.Platform.Storage.Models;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Shared;

namespace Fleet.Platform.Storage;

public sealed class JsonAgentStore : IAgentStore
{
    public void Save(string project, AgentRecord agent)
    {
        var file = FileFor(project);

        if (file is null || agent.Worktree.Length == 0)
        {
            return;
        }

        Mutate(file, session =>
        {
            session.Agents.RemoveAll(a => SameWorktree(a.Worktree, agent.Worktree));
            session.Agents.Add(new AgentEntry
            {
                Worktree = HomePath.Contract(agent.Worktree),
                Repository = agent.Repository,
                Branch = agent.Branch,
                Harness = agent.Harness,
                BaseRef = agent.BaseRef,
                RepositoryWasBare = agent.RepositoryWasBare,
                Hidden = agent.Hidden,
                Open = agent.Open,
                Owner = agent.Owner,
                Status = agent.Status,
            });
        });
    }

    public IReadOnlyList<AgentRecord> List(string project)
    {
        var file = FileFor(project);

        if (file is null)
        {
            return [];
        }

        return Read(file).Agents
            .Select(a => new AgentRecord(
                HomePath.Expand(a.Worktree),
                a.Repository,
                a.Branch,
                a.Harness,
                a.BaseRef,
                a.RepositoryWasBare,
                a.Hidden,
                a.Open,
                a.Owner,
                a.Status))
            .OrderBy(a => a.Repository, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.Branch, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void Remove(string project, string worktree)
    {
        var file = FileFor(project);

        if (file is null)
        {
            return;
        }

        Mutate(file, session => session.Agents.RemoveAll(a => SameWorktree(a.Worktree, worktree)));
    }

    private static bool SameWorktree(string stored, string candidate) =>
        PathKey.Same(stored, candidate);

    private static void Mutate(string file, Action<SessionFile> change)
    {
        FleetPaths.EnsureDirs();

        using var gate = Lock(file + ".lock");

        var session = Read(file);
        change(session);
        Write(file, session);
    }

    private static FileStream? Lock(string lockPath)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);

        while (true)
        {
            try
            {
                return new FileStream(
                    lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(20);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    private static SessionFile Read(string file)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (!File.Exists(file))
                {
                    return new SessionFile();
                }

                return JsonSerializer.Deserialize(
                    File.ReadAllText(file), FleetJsonContext.Default.SessionFile) ?? new SessionFile();
            }
            catch (IOException) when (attempt < 3)
            {
                Thread.Sleep(15);
            }
            catch (Exception e)
                when (e is IOException or JsonException or UnauthorizedAccessException)
            {
                return new SessionFile();
            }
        }
    }

    private static void Write(string file, SessionFile session)
    {
        try
        {
            FleetPaths.EnsureDirs();

            var temp = file + ".tmp";

            File.WriteAllText(
                temp, JsonSerializer.Serialize(session, FleetJsonContext.Default.SessionFile));
            File.Move(temp, file, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string? FileFor(string project)
    {
        var name = ProjectName.Sanitize(project);

        return name.Length == 0 ? null : Path.Combine(FleetPaths.Sessions, name + ".json");
    }
}
