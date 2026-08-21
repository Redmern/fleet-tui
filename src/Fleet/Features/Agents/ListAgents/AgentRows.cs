using Fleet.Ports.Agents.Models;
using Fleet.Shared;
using Fleet.Ui;
using Fleet.Ui.Constants;
using Fleet.Ui.Models;

namespace Fleet.Features.Agents.ListAgents;

public static class AgentRows
{
    public const string EmptyHint = "(no agents - press 'n' to start one)";

    public static IReadOnlyList<FleetRow> For(IReadOnlyList<AgentRecord> agents) =>
        For(agents, _ => BranchState.Unknown);

    public static IReadOnlyList<FleetRow> For(
        IReadOnlyList<AgentRecord> agents, Func<AgentRecord, BranchState> state)
    {
        if (agents.Count == 0)
        {
            return [FleetRow.Plain(EmptyHint)];
        }

        var pills = agents.Select(a => BranchStatus.Pill(a.Branch, state(a))).ToList();
        var pillWidth = pills.Max(p => p.Sum(s => s.Text.Length));

        return [.. agents.Select((a, i) => Row(a, pills[i], pillWidth))];
    }

    private static FleetRow Row(
        AgentRecord agent, IReadOnlyList<FleetSpan> pill, int pillWidth)
    {
        var gap = pillWidth - pill.Sum(s => s.Text.Length);

        List<FleetSpan> spans =
        [
            .. pill,
            FleetSpan.Plain(new string(' ', gap + 3)),
            FleetSpan.Muted(agent.Repository),
        ];

        List<FleetSpan> trailing = [];

        if (agent.Status.Length > 0)
        {
            trailing.Add(new FleetSpan($"{agent.Status}   ", ToneFor(agent.Status)));
        }

        if (agent.Hidden)
        {
            trailing.Add(FleetSpan.Muted($"{FleetGlyphs.Hidden} "));
        }

        return new FleetRow(spans, trailing.Count == 0 ? null : trailing);
    }

    private static string ToneFor(string status) => status switch
    {
        AgentActivity.Working => FleetTones.Warn,
        AgentActivity.Waiting => FleetTones.Bad,
        AgentActivity.Idle => FleetTones.Good,
        _ => FleetTones.Muted,
    };
}
