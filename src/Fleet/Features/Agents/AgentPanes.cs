using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;

namespace Fleet.Features.Agents;

public static class AgentPanes
{
    public static bool Owns(Pane pane, AgentRecord agent) => AgentPaneMatch.Owns(pane, agent);

    public static bool Shown(AgentRecord agent, IEnumerable<Pane> panes) =>
        panes.Any(p => Owns(p, agent) && !FleetWorkspaces.IsHidden(p.SessionName));
}
