using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux.Models;

namespace Fleet.Features.Agents;

public static class SubBrowse
{
    public static bool Is(Pane pane) =>
        pane.PaneTitle.EndsWith(" files", StringComparison.OrdinalIgnoreCase);

    public static string Title(AgentRecord agent) => AgentPaneMatch.BrowserTitle(agent);
}
