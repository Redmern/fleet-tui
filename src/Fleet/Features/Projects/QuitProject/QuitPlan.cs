using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux.Models;
using Fleet.Shared;

namespace Fleet.Features.Projects.QuitProject;

public static class QuitPlan
{
    public static IReadOnlyList<PaneId> PanesToClose(
        IReadOnlyList<Pane> panes,
        string? projectWindow,
        IReadOnlyList<AgentRecord> agents)
    {
        var doomed = new List<PaneId>();

        foreach (var pane in panes)
        {
            var inWindow = projectWindow is not null
                && string.Equals(pane.WindowId, projectWindow, StringComparison.Ordinal);

            var isAgent = agents.Any(a => PathKey.Same(pane.Cwd, a.Worktree));

            if ((inWindow || isAgent) && !doomed.Contains(pane.Id))
            {
                doomed.Add(pane.Id);
            }
        }

        return doomed;
    }
}
