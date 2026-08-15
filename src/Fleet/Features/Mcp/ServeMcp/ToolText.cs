using Fleet.Ports.Agents.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;

namespace Fleet.Features.Mcp.ServeMcp;

public static class ToolText
{
    public static string Agents(IReadOnlyList<AgentRecord> agents)
    {
        var visible = agents
            .Where(a => !AgentHarness.IsOrchestrator(a.Harness))
            .ToList();

        if (visible.Count == 0)
        {
            return "No agents yet.";
        }

        return string.Join('\n', visible.Select(Line));
    }

    public static string Repositories(IReadOnlyList<string> names) =>
        names.Count == 0 ? "No repositories yet." : string.Join('\n', names);

    public static string Agent(AgentRecord agent, BranchState state) =>
        $"{AgentKey.Describe(agent.Repository, agent.Branch)}: {Where(agent)}, {Drift(state)}";

    public static string NotFound(string repository, string branch) =>
        $"No agent {AgentKey.Describe(repository, branch)} in this project.";

    private static string Line(AgentRecord agent)
    {
        var where = agent.Hidden ? "hidden" : agent.Open ? "open" : "closed";

        return $"{AgentKey.Describe(agent.Repository, agent.Branch)} — {where}";
    }

    private static string Where(AgentRecord agent) =>
        agent.Hidden ? "hidden" : agent.Open ? "open" : "closed";

    private static string Drift(BranchState state)
    {
        var parts = new List<string>();

        if (state.Ahead > 0)
        {
            parts.Add($"{state.Ahead} ahead");
        }

        if (state.Behind > 0)
        {
            parts.Add($"{state.Behind} behind");
        }

        if (state.Dirty)
        {
            parts.Add("dirty");
        }

        return parts.Count == 0 ? "in sync" : string.Join(", ", parts);
    }
}
