using Fleet.Ports.Agents;

namespace Fleet.Features.Agents.CleanupAgents;

public sealed class CleanupHandler(IAgentStore store)
{
    public string Handle(string project)
    {
        var stale = store.List(project)
            .Where(a => !Directory.Exists(a.Worktree))
            .ToList();

        foreach (var record in stale)
        {
            store.Remove(project, record.Worktree);
        }

        return stale.Count == 0
            ? "nothing stale to clean up."
            : $"cleaned up {stale.Count} agent record(s) whose folder is gone: "
              + string.Join(", ", stale.Select(a => a.Branch));
    }
}
