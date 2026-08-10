using Fleet.Ports.Agents.Models;
using Fleet.Ports.Git.Models;
using Fleet.Ui.Constants;

namespace Fleet.Features.Agents.ListAgents;

public static class AgentRows
{
    public const string EmptyHint = "(no agents - press 'n' to start one)";

    public static IReadOnlyList<string> For(IReadOnlyList<AgentRecord> agents) =>
        For(agents, _ => BranchState.Unknown);

    public static IReadOnlyList<string> For(
        IReadOnlyList<AgentRecord> agents, Func<AgentRecord, BranchState> state)
    {
        if (agents.Count == 0)
        {
            return [EmptyHint];
        }

        var pills = agents.Select(a => Pill(a.Branch, state(a))).ToList();

        var repoWidth = agents.Max(a => a.Repository.Length);
        var pillWidth = pills.Max(p => p.Length);

        return
        [
            .. agents.Select((a, i) =>
                $"{a.Repository.PadRight(repoWidth)}   {pills[i].PadRight(pillWidth)}   " +
                $"{a.Harness.PadRight(6)}" + (a.Hidden ? "   (hidden)" : string.Empty)),
        ];
    }

    public static string Pill(string branch, BranchState state)
    {
        var diffs = state.Ahead > 0 ? $" +{state.Ahead}" : string.Empty;
        var dirty = state.Dirty ? "*" : string.Empty;

        return $"{FleetGlyphs.Branch} {branch}{diffs}{dirty}";
    }
}
