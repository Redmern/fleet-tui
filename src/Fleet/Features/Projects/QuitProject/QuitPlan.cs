using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux.Models;
using Fleet.Shared;
using Fleet.Shared.Todos;

namespace Fleet.Features.Projects.QuitProject;

public static class QuitPlan
{
    public static IReadOnlyList<PaneId> PanesToClose(
        IReadOnlyList<Pane> panes,
        string projectRoot,
        IReadOnlyList<AgentRecord> agents)
    {
        var doomed = new List<PaneId>();
        var todos = TodoPaths.Folder(projectRoot);

        foreach (var pane in panes)
        {
            var isProject = PathKey.Same(pane.Cwd, projectRoot) || PathKey.Same(pane.Cwd, todos);
            var isAgent = agents.Any(a =>
                AgentPaneMatch.Owns(pane, a) || AgentPaneMatch.IsEditor(pane, a));

            if ((isProject || isAgent) && !doomed.Contains(pane.Id))
            {
                doomed.Add(pane.Id);
            }
        }

        return doomed;
    }
}
