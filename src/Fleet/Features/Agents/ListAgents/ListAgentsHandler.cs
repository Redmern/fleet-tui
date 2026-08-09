using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;

namespace Fleet.Features.Agents.ListAgents;

public sealed class ListAgentsHandler(IAgentStore store)
{
    public IReadOnlyList<AgentRecord> Handle(string project) => store.List(project);
}
