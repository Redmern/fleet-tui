using Fleet.Ports.Agents.Models;
using Fleet.Ui;

using Fleet.Shared;

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

        var pills = agents.Select(a => BranchStatus.Pill(a.Branch)).ToList();
        var pillWidth = pills.Max(p => p.Length);
        var repoWidth = agents.Max(a => a.Repository.Length);

        return
        [
            .. agents.Select((a, i) =>
                $"{pills[i].PadRight(pillWidth)}   {a.Repository.PadRight(repoWidth)}   " +
                $"{BranchStatus.Of(state(a))}" + (a.Hidden ? "   (hidden)" : string.Empty)),
        ];
    }
}
