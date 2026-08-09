using Fleet.Ports.Agents.Models;

namespace Fleet.Features.Agents.ListAgents;

public static class AgentRows
{
    public const string EmptyHint = "(no agents - press 'n' to start one)";

    public static IReadOnlyList<string> For(IReadOnlyList<AgentRecord> agents)
    {
        if (agents.Count == 0)
        {
            return [EmptyHint];
        }

        var repoWidth = agents.Max(a => a.Repository.Length);
        var branchWidth = agents.Max(a => a.Branch.Length);

        return agents
            .Select(a =>
                $"{a.Repository.PadRight(repoWidth)}   " +
                $"{a.Branch.PadRight(branchWidth)}   {a.Harness}")
            .ToList();
    }
}
