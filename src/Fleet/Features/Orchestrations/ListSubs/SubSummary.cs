using Fleet.Ports.Agents.Models;
using Fleet.Shared.Constants;

namespace Fleet.Features.Orchestrations.ListSubs;

public static class SubSummary
{
    public const string None = "No sub-orchestrators yet.";

    public const string NoUnowned = "No agents outside a sub-orchestrator.";

    public static string Text(IReadOnlyList<AgentRecord> agents, Func<AgentRecord, bool> paneOpen)
    {
        var listing = SubTree.Of(agents);

        var subs = listing.Flat
            .Where(e => !e.IsChild)
            .Select(e => e.Group)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (subs.Count == 0)
        {
            return None;
        }

        var lines = new List<string>();
        var group = string.Empty;

        foreach (var entry in listing.Flat)
        {
            if (!entry.IsChild)
            {
                group = entry.Group;
                lines.AddRange(Sub(entry.Agent, SubStatus.ChildrenOf(listing, entry.Agent), paneOpen(entry.Agent)));
                continue;
            }

            if (!subs.Contains(entry.Group) && !string.Equals(group, entry.Group, StringComparison.OrdinalIgnoreCase))
            {
                group = entry.Group;
                lines.Add($"{entry.Group} — no longer registered; its agents remain");
            }

            lines.AddRange(Child(entry.Agent, paneOpen(entry.Agent), "  "));
        }

        return string.Join('\n', lines);
    }

    public static string Unowned(IReadOnlyList<AgentRecord> agents, Func<AgentRecord, bool> paneOpen)
    {
        var board = SubTree.Of(agents).Board
            .OrderBy(a => a.Repository, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.Branch, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return board.Count == 0
            ? NoUnowned
            : string.Join('\n', board.SelectMany(a => Child(a, paneOpen(a), string.Empty)));
    }

    private static IEnumerable<string> Sub(AgentRecord sub, IEnumerable<AgentRecord> children, bool open)
    {
        var status = SubStatus.Shown(sub, children, open);
        var where = open ? "pane open" : "pane closed";
        var hidden = sub.Hidden ? ", hidden" : string.Empty;

        yield return $"{sub.Branch} — {status}, {where}{hidden}";

        if (LastReport(sub) is { } report)
        {
            yield return $"  {report}";
        }
    }

    private static IEnumerable<string> Child(AgentRecord agent, bool open, string indent)
    {
        var where = agent.Hidden ? "hidden" : open ? "open" : "closed";
        var status = agent.Status.Trim().Length == 0 ? "no report" : agent.Status.Trim();

        yield return $"{indent}- {agent.Repository}/{agent.Branch} — {where}, {status}";

        if (LastReport(agent) is { } report)
        {
            yield return $"{indent}    {report}";
        }
    }

    private static string? LastReport(AgentRecord agent)
    {
        if (agent.ReportedAt.Length == 0 && agent.Summary.Length == 0)
        {
            return null;
        }

        var at = agent.ReportedAt.Length == 0 ? string.Empty : $" {agent.ReportedAt}";
        var summary = agent.Summary.Length == 0 ? string.Empty : $": {agent.Summary}";

        return $"last report{at}{summary}";
    }
}
