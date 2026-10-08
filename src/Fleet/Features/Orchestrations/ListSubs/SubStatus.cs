using Fleet.Features.Orchestrations.ListSubs.Models;
using Fleet.Ports.Agents.Models;
using Fleet.Shared.Constants;

namespace Fleet.Features.Orchestrations.ListSubs;

public static class SubStatus
{
    public const string Idle = "idle";

    public static string? Derive(AgentRecord sub, IEnumerable<AgentRecord> children)
    {
        var own = OrchestrationStatus.Normalize(sub.Status);

        if (own is not (OrchestrationStatus.Done or OrchestrationStatus.Failed))
        {
            return null;
        }

        var visible = children.Where(c => !c.Hidden).Select(c => c.Status.Trim().ToLowerInvariant()).ToList();

        if (visible.Any(s => s is not (OrchestrationStatus.Done or OrchestrationStatus.Failed)))
        {
            return Idle;
        }

        return visible.Contains(OrchestrationStatus.Failed) ? OrchestrationStatus.Failed : own;
    }

    public static IEnumerable<AgentRecord> ChildrenOf(SubListing listing, AgentRecord sub) =>
        listing.Flat
            .Where(e => e.IsChild && string.Equals(e.Group, sub.Branch, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Agent);
}
