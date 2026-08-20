using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux.Models;
using Fleet.Shared;

namespace Fleet.Features.Agents;

public static class AgentPanes
{
    public static bool Owns(Pane pane, AgentRecord agent) =>
        PathKey.Same(pane.Cwd, agent.Worktree)
        || string.Equals(
            pane.Title,
            AgentTitle.For(agent.Repository, agent.Branch),
            StringComparison.OrdinalIgnoreCase)
        || string.Equals(pane.Title, SubBrowse.Title(agent), StringComparison.OrdinalIgnoreCase);
}
