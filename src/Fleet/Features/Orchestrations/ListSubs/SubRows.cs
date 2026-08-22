using Fleet.Features.Orchestrations.ListSubs.Models;
using Fleet.Ports.Agents.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;
using Fleet.Ui;
using Fleet.Ui.Constants;
using Fleet.Ui.Models;

namespace Fleet.Features.Orchestrations.ListSubs;

public static class SubRows
{
    public static string EmptyHint(string trigger) =>
        $"(no sub-orchestrators - type '{trigger} <task>' in the main pane)";

    public static IReadOnlyList<FleetRow> For(
        SubListing listing, Func<AgentRecord, BranchState> state, string trigger)
    {
        if (listing.Flat.Count == 0)
        {
            return [FleetRow.Plain(EmptyHint(trigger))];
        }

        var children = listing.Flat.Where(e => e.IsChild).Select(e => e.Agent).ToList();

        var pillWidth = children.Count == 0
            ? 0
            : children.Max(a => BranchStatus.Pill(a.Branch, state(a)).Sum(s => s.Text.Length));

        return [.. listing.Flat.Select(e => e.IsChild
            ? Child(e.Agent, state, pillWidth)
            : Orchestrator(e.Agent))];
    }

    private static FleetRow Orchestrator(AgentRecord agent)
    {
        List<FleetSpan> trailing =
        [
            new FleetSpan(agent.Status.Length == 0 ? OrchestrationStatus.Working : agent.Status,
                ToneFor(agent.Status)),
        ];

        if (agent.Hidden)
        {
            trailing.Add(FleetSpan.Muted($"   {FleetGlyphs.Hidden} "));
        }

        return new FleetRow(
            [new FleetSpan($"{FleetGlyphs.Orchestrator}  ", FleetTones.Normal), FleetSpan.Plain(agent.Branch)],
            trailing);
    }

    private static FleetRow Child(
        AgentRecord agent, Func<AgentRecord, BranchState> state, int pillWidth)
    {
        var pill = BranchStatus.Pill(agent.Branch, state(agent));
        var gap = pillWidth - pill.Sum(s => s.Text.Length);

        List<FleetSpan> spans =
        [
            FleetSpan.Muted($"  {FleetGlyphs.Child} "),
            .. pill,
            FleetSpan.Plain(new string(' ', gap + 3)),
            FleetSpan.Muted(agent.Repository),
        ];

        return new FleetRow(
            spans,
            agent.Hidden ? [FleetSpan.Muted($"{FleetGlyphs.Hidden} ")] : null);
    }

    private static string ToneFor(string status) => OrchestrationStatus.Normalize(status) switch
    {
        OrchestrationStatus.Done => FleetTones.Good,
        OrchestrationStatus.Failed => FleetTones.Bad,
        _ => FleetTones.Warn,
    };
}
