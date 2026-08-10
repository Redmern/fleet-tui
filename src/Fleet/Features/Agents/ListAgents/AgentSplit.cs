using Fleet.Features.Agents.ListAgents.Models;
using Fleet.Ports.Agents.Models;

namespace Fleet.Features.Agents.ListAgents;

public static class AgentSplit
{
    public static AgentListing By(IReadOnlyList<AgentRecord> agents) =>
        new(
            [.. agents.Where(a => !a.Hidden)],
            [.. agents.Where(a => a.Hidden)]);
}
