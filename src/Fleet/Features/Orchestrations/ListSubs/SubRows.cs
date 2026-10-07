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

    public static IReadOnlyList<int> GapsAfter(SubListing listing) =>
        [.. Enumerable.Range(0, Math.Max(0, listing.Flat.Count - 1))
            .Where(i => !listing.Flat[i + 1].IsChild)];

    private static FleetRow Orchestrator(AgentRecord agent)
    {
        var icon = StatusIcon.For(StatusOf(agent))!;

        List<FleetSpan> trailing = [icon with { Text = $"{icon.Text}  " }, FleetHiddenMark.For(agent.Hidden)];

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
            [FleetHiddenMark.For(agent.Hidden)]);
    }

    public static IReadOnlyList<string> Statuses(SubListing listing) =>
        [.. listing.Flat.Select(e => e.IsChild ? string.Empty : StatusOf(e.Agent))];

    private static string StatusOf(AgentRecord agent) =>
        StatusIcon.For(agent.Status) is null
            ? OrchestrationStatus.Normalize(agent.Status)
            : agent.Status.Trim().ToLowerInvariant();
}
