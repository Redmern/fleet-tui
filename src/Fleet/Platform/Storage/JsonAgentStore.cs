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

        var session = Read(file);

        session.Agents.RemoveAll(a => SameWorktree(a.Worktree, agent.Worktree));
        session.Agents.Add(new AgentEntry
        {
            Worktree = HomePath.Contract(agent.Worktree),
            Repository = agent.Repository,
            Branch = agent.Branch,
            Harness = agent.Harness,
            BaseRef = agent.BaseRef,
            RepositoryWasBare = agent.RepositoryWasBare,
        });

        Write(file, session);
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
                a.RepositoryWasBare))
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

        var session = Read(file);

        if (session.Agents.RemoveAll(a => SameWorktree(a.Worktree, worktree)) > 0)
        {
            Write(file, session);
        }
    }

    private static bool SameWorktree(string stored, string candidate) =>
        PathKey.Same(stored, candidate);

    private static SessionFile Read(string file)
    {
        try
        {
            return JsonSerializer.Deserialize(
                File.ReadAllText(file), FleetJsonContext.Default.SessionFile) ?? new SessionFile();
        }
        catch (Exception e)
            when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new SessionFile();
        }
    }

    private static void Write(string file, SessionFile session)
    {
        try
        {
            FleetPaths.EnsureDirs();
            File.WriteAllText(
                file, JsonSerializer.Serialize(session, FleetJsonContext.Default.SessionFile));
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
