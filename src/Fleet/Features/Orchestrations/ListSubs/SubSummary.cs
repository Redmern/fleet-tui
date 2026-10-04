using Fleet.Ports.Agents.Models;
using Fleet.Shared.Constants;

namespace Fleet.Features.Orchestrations.ListSubs;

public static class SubSummary
{
    public const string None = "No sub-orchestrators yet.";

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
                lines.AddRange(Sub(entry.Agent, paneOpen(entry.Agent)));
                continue;
            }

            if (!subs.Contains(entry.Group) && !string.Equals(group, entry.Group, StringComparison.OrdinalIgnoreCase))
            {
                group = entry.Group;
                lines.Add($"{entry.Group} — no longer registered; its agents remain");
            }

            lines.Add(Child(entry.Agent, paneOpen(entry.Agent)));
        }

        return string.Join('\n', lines);
    }

    private static IEnumerable<string> Sub(AgentRecord sub, bool open)
    {
        var where = open ? "pane open" : "pane closed";
        var hidden = sub.Hidden ? ", hidden" : string.Empty;

        yield return $"{sub.Branch} — {OrchestrationStatus.Normalize(sub.Status)}, {where}{hidden}";

        if (sub.ReportedAt.Length > 0 || sub.Summary.Length > 0)
        {
            var at = sub.ReportedAt.Length == 0 ? string.Empty : $" {sub.ReportedAt}";
            var summary = sub.Summary.Length == 0 ? string.Empty : $": {sub.Summary}";

            yield return $"  last report{at}{summary}";
        }
    }

    private static string Child(AgentRecord agent, bool open)
    {
        var where = agent.Hidden ? "hidden" : open ? "open" : "closed";
        var status = agent.Status.Trim().Length == 0 ? "no report" : agent.Status.Trim();

        return $"  - {agent.Repository}/{agent.Branch} — {where}, {status}";
    }
}
