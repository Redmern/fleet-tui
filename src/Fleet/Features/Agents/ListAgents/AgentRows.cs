using Fleet.Ports.Agents.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;
using Fleet.Shared.Evidence;
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

    private static IEnumerable<FleetSpan> Evidence(AgentRecord agent)
    {
        if (agent.Evidence is { ShortHead.Length: > 0 } evidence)
        {
            yield return FleetSpan.Muted($"  {evidence.ShortHead}");
        }

        if (OrchestrationStatus.Normalize(agent.Status) is not (OrchestrationStatus.Done or OrchestrationStatus.Failed)
            || agent.Status.Trim().Length == 0)
        {
            yield break;
        }

        var (text, tone) = agent.Evidence?.Verification switch
        {
            Verification.Ok => ("verified", FleetTones.Good),
            Verification.Failed => ("verify failed", FleetTones.Bad),
            _ when AgentHarness.IsOrchestrator(agent.Harness) => (string.Empty, FleetTones.Normal),
            _ => ("unverified", FleetTones.Warn),
        };

        if (text.Length > 0)
        {
            yield return new FleetSpan($"  {text}", tone);
        }
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
            .. Evidence(agent),
        ];

        List<FleetSpan> trailing = [];

        if (agent.Status.Length > 0)
        {
            var icon = StatusIcon.For(agent.Status) ?? FleetSpan.Muted(FleetGlyphs.Dirty);
            trailing.Add(icon with { Text = $"{icon.Text}  " });
        }

        trailing.Add(FleetHiddenMark.For(agent.Hidden));

        return new FleetRow(spans, trailing);
    }
}
