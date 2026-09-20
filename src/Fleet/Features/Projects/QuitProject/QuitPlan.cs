using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux.Models;
using Fleet.Shared;

namespace Fleet.Features.Projects.QuitProject;

public static class QuitPlan
{
    public static IReadOnlyList<PaneId> PanesToClose(
        IReadOnlyList<Pane> panes,
        string projectRoot,
        IReadOnlyList<AgentRecord> agents)
    {
        var doomed = new List<PaneId>();

        foreach (var pane in panes)
        {
            var isRoot = PathKey.Same(pane.Cwd, projectRoot);
            var isAgent = agents.Any(a => AgentPaneMatch.Owns(pane, a));

            if ((isRoot || isAgent) && !doomed.Contains(pane.Id))
            {
                doomed.Add(pane.Id);
            }
        }

        return doomed;
    }
}
