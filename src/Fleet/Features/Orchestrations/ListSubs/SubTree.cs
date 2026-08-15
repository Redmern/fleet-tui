using Fleet.Features.Orchestrations.ListSubs.Models;
using Fleet.Ports.Agents.Models;
using Fleet.Shared.Constants;

namespace Fleet.Features.Orchestrations.ListSubs;

public static class SubTree
{
    public static SubListing Of(IReadOnlyList<AgentRecord> agents)
    {
        var orchestrators = agents
            .Where(a => AgentHarness.IsOrchestrator(a.Harness))
            .OrderBy(a => a.Branch, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var owners = orchestrators.Select(o => o.Branch).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var board = agents
            .Where(a => !AgentHarness.IsOrchestrator(a.Harness) && a.Owner.Length == 0)
            .ToList();

        var children = agents
            .Where(a => !AgentHarness.IsOrchestrator(a.Harness) && a.Owner.Length > 0)
            .ToLookup(a => a.Owner, StringComparer.OrdinalIgnoreCase);

        var flat = new List<SubEntry>();

        foreach (var orchestrator in orchestrators)
        {
            flat.Add(new SubEntry(orchestrator, IsChild: false, orchestrator.Branch));
            flat.AddRange(ChildrenOf(children[orchestrator.Branch], orchestrator.Branch));
        }

        foreach (var orphanOwner in children
            .Select(g => g.Key)
            .Where(o => !owners.Contains(o))
            .OrderBy(o => o, StringComparer.OrdinalIgnoreCase))
        {
            flat.AddRange(ChildrenOf(children[orphanOwner], orphanOwner));
        }

        return new SubListing(board, flat);
    }

    private static IEnumerable<SubEntry> ChildrenOf(IEnumerable<AgentRecord> agents, string group) =>
        agents
            .OrderBy(a => a.Repository, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.Branch, StringComparer.OrdinalIgnoreCase)
            .Select(a => new SubEntry(a, IsChild: true, group));
}
