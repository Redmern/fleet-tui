using System.Text.Json;
using Fleet.Platform.Storage.Models;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Shared;
using Fleet.Shared.Evidence;
using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Models;

namespace Fleet.Platform.Storage;

public sealed class JsonAgentStore(TimeSpan lockPatience, TimeSpan busyPatience) : IAgentStore
{
    public static readonly TimeSpan LockPatience = TimeSpan.FromSeconds(5);

    public JsonAgentStore()
        : this(LockPatience, BusyFiles.Patience)
    {
    }

    public void Save(string project, AgentRecord agent)
    {
        var file = FileFor(project);

        if (file is null || agent.Worktree.Length == 0)
        {
            return;
        }

        Mutate(file, unlockedIsFine: true, session =>
        {
            session.Agents.RemoveAll(a => SameWorktree(a.Worktree, agent.Worktree));
            session.Agents.Add(Entry(agent));
            return true;
        });
    }

    public bool Replace(string project, AgentRecord agent)
    {
        var file = FileFor(project);

        if (file is null || agent.Worktree.Length == 0)
        {
            return false;
        }

        return Mutate(file, unlockedIsFine: false, session =>
        {
            var index = session.Agents.FindIndex(a => SameWorktree(a.Worktree, agent.Worktree));

            if (index < 0)
            {
                return false;
            }

            session.Agents[index] = Entry(agent);
            return true;
        });
    }

    public bool Update(string project, string worktree, Func<AgentRecord, AgentRecord> change)
    {
        var file = FileFor(project);

        if (file is null || worktree.Length == 0)
        {
            return false;
        }

        return Mutate(file, unlockedIsFine: false, session =>
        {
            var index = session.Agents.FindIndex(a => SameWorktree(a.Worktree, worktree));

            if (index < 0)
            {
                return false;
            }

            session.Agents[index] = Entry(change(Record(session.Agents[index])));
            return true;
        });
    }

    private static AgentEntry Entry(AgentRecord agent) =>
        new()
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
            HostInNvim = agent.HostInNvim,
            InNvim = agent.InNvim,
            Summary = agent.Summary,
            ReportedAt = agent.ReportedAt,
            Model = agent.Model?.Model,
            Effort = agent.Model?.Effort,
            OrchestratorHarness = agent.OrchestratorHarness.Length == 0 ? null : agent.OrchestratorHarness,
            InstructedAt = agent.InstructedAt.Length == 0 ? null : agent.InstructedAt,
            Evidence = agent.Evidence is { } e ? EvidenceOf(e) : null,
        };

    private static EvidenceEntry EvidenceOf(ReportEvidence e) =>
        new()
        {
            Worktree = HomePath.Contract(e.Worktree),
            BaseRef = e.BaseRef,
            MergeBase = e.MergeBase,
            Head = e.Head,
            Dirty = e.Dirty,
            VerifyCommand = e.VerifyCommand.Length == 0 ? null : e.VerifyCommand,
            VerifyExitCode = e.VerifyExitCode,
            VerifyOutput = e.VerifyOutput.Length == 0 ? null : e.VerifyOutput,
        };

    public IReadOnlyList<AgentRecord> List(string project)
    {
        var file = FileFor(project);

        if (file is null)
        {
            return [];
        }

        return Read(file).Agents
            .Select(Record)
            .OrderBy(a => a.Repository, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.Branch, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public AgentLocation? FindByWorktree(string path)
    {
        if (path.Length == 0 || !Directory.Exists(FleetPaths.Sessions))
        {
            return null;
        }

        AgentLocation? best = null;
        var bestLength = -1;

        try
        {
            foreach (var file in Directory.EnumerateFiles(FleetPaths.Sessions, "*.json"))
            {
                foreach (var entry in Read(file).Agents.Where(a => PathKey.Within(path, a.Worktree)))
                {
                    var length = PathKey.For(entry.Worktree).Length;

                    if (length > bestLength)
                    {
                        best = new AgentLocation(Path.GetFileNameWithoutExtension(file), Record(entry));
                        bestLength = length;
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        return best;
    }

    private static AgentRecord Record(AgentEntry a)
    {
        var (harness, hostInNvim) = AgentRecord.Migrated(a.Harness, a.HostInNvim, a.Claude, a.Owner);

        return new AgentRecord(
            HomePath.Expand(a.Worktree),
            a.Repository,
            a.Branch,
            harness,
            a.BaseRef,
            a.RepositoryWasBare,
            a.Hidden,
            a.Open,
            a.Owner,
            a.Status,
            hostInNvim,
            a.InNvim,
            a.Summary,
            a.ReportedAt,
            a.Model is null ? null : new RoleModel(a.Model, a.Effort ?? ModelChoice.Inherit),
            a.OrchestratorHarness ?? string.Empty,
            a.InstructedAt ?? string.Empty,
            a.Evidence is { } e
                ? new ReportEvidence(
                    HomePath.Expand(e.Worktree),
                    e.BaseRef,
                    e.MergeBase,
                    e.Head,
                    e.Dirty,
                    e.VerifyCommand ?? string.Empty,
                    e.VerifyExitCode,
                    e.VerifyOutput ?? string.Empty)
                : null);
    }

    public bool Remove(string project, string worktree)
    {
        var file = FileFor(project);

        if (file is null)
        {
            return false;
        }

        return Mutate(file, unlockedIsFine: false, session =>
        {
            session.Agents.RemoveAll(a => SameWorktree(a.Worktree, worktree));
            return true;
        });
    }

    private static bool SameWorktree(string stored, string candidate) =>
        PathKey.Same(stored, candidate);

    private bool Mutate(string file, bool unlockedIsFine, Func<SessionFile, bool> change)
    {
        FleetPaths.EnsureDirs();

        using var gate = Lock(file + ".lock");

        if ((gate is null && !unlockedIsFine) || TryRead(file, busyPatience) is not { } session)
        {
            return false;
        }

        return change(session) && Write(file, session);
    }

    private FileStream? Lock(string lockPath)
    {
        var deadline = DateTime.UtcNow + lockPatience;

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

    private static SessionFile Read(string file) =>
        TryRead(file, TimeSpan.FromMilliseconds(50)) ?? new SessionFile();

    private static SessionFile? TryRead(string file, TimeSpan patience) =>
        BusyFiles.Retry(() =>
        {
            if (!File.Exists(file))
            {
                return new SessionFile();
            }

            try
            {
                return JsonSerializer.Deserialize(
                    File.ReadAllText(file), FleetJsonContext.Default.SessionFile) ?? new SessionFile();
            }
            catch (JsonException)
            {
                return new SessionFile();
            }
        }, patience);

    private static bool Write(string file, SessionFile session)
    {
        var json = JsonSerializer.Serialize(session, FleetJsonContext.Default.SessionFile);

        return BusyFiles.Replace(file, temp => File.WriteAllText(temp, json));
    }

    private static string? FileFor(string project)
    {
        var name = ProjectName.Sanitize(project);

        return name.Length == 0 ? null : Path.Combine(FleetPaths.Sessions, name + ".json");
    }
}
