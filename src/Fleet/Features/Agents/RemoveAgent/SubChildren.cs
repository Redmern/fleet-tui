using Fleet.Ports.Agents.Models;
using Fleet.Shared.Constants;

namespace Fleet.Features.Agents.RemoveAgent;

public static class SubChildren
{
    public static IReadOnlyList<AgentRecord> Of(IReadOnlyList<AgentRecord> agents, string slug) =>
        [.. agents.Where(a => !AgentHarness.IsOrchestrator(a.Harness)
                              && string.Equals(a.Owner, slug, StringComparison.OrdinalIgnoreCase))];

    public static AgentRecord Released(AgentRecord child) =>
        child with { Owner = string.Empty, Claude = child.RunsClaude };
}
