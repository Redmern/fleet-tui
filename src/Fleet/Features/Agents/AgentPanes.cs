using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux.Models;
using Fleet.Shared;

namespace Fleet.Features.Agents;

public static class AgentPanes
{
    public static bool Owns(Pane pane, AgentRecord agent) => AgentPaneMatch.Owns(pane, agent);
}
