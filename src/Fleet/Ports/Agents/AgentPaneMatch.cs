using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;

namespace Fleet.Ports.Agents;

public static class AgentPaneMatch
{
    public static string BrowserTitle(AgentRecord agent) =>
        $"{AgentTitle.For(agent.Repository, agent.Branch)} files";

    public static string EditorTitle(AgentRecord agent) =>
        $"{AgentTitle.For(agent.Repository, agent.Branch)} editor";

    public static bool IsEditor(Pane pane, AgentRecord agent) =>
        string.Equals(pane.PaneTitle, EditorTitle(agent), StringComparison.OrdinalIgnoreCase);

    public static bool Owns(Pane pane, AgentRecord agent) =>
        !string.Equals(pane.Title, FleetTabTitles.Dashboard, StringComparison.OrdinalIgnoreCase)
        && !IsEditor(pane, agent)
        && (PathKey.Same(pane.Cwd, agent.Worktree)
            || string.Equals(
                pane.Title,
                AgentTitle.For(agent.Repository, agent.Branch),
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(pane.Title, BrowserTitle(agent), StringComparison.OrdinalIgnoreCase));
}
