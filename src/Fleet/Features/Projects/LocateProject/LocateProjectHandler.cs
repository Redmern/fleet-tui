using Fleet.Features.Projects.LocateProject.Models;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Enums;
using Fleet.Ports.Mux.Models;
using Fleet.Ports.Projects.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;

namespace Fleet.Features.Projects.LocateProject;

public sealed class LocateProjectHandler(IMuxDriver mux)
{
    public async Task<IReadOnlyDictionary<string, ProjectLocation>> HandleAsync(
        IReadOnlyList<Project> projects, CancellationToken ct = default)
    {
        var located = new Dictionary<string, ProjectLocation>(StringComparer.OrdinalIgnoreCase);

        if (mux.Caps.HasFlag(MuxCaps.Workspaces))
        {
            var workspaces = await mux.ListWorkspacesAsync(ct).ConfigureAwait(false);

            foreach (var project in projects)
            {
                var own = workspaces.FirstOrDefault(w =>
                    string.Equals(w.Name, project.Name, StringComparison.OrdinalIgnoreCase));

                located[project.Name] = own is null
                    ? ProjectLocation.Closed
                    : new ProjectLocation(true, own.ShownHere, own.InWindow || own.ShownHere, own.InOtherWindow);
            }

            return located;
        }

        var panes = await mux.ListPanesAsync(ct).ConfigureAwait(false);
        var window = panes.FirstOrDefault(p => p.Id == mux.CurrentPane)?.WindowId;

        foreach (var project in projects)
        {
            var mine = panes.Where(p => PathKey.Same(p.Cwd, project.Root)).ToList();

            var here = window is not null && mine.Any(p => p.WindowId == window && !IsHidden(p));
            located[project.Name] = new ProjectLocation(
                mine.Count > 0,
                here,
                here,
                mine.Any(p => p.WindowId != window && !IsHidden(p)));
        }

        return located;
    }

    private static bool IsHidden(Pane pane) =>
        string.Equals(pane.SessionName, FleetWorkspaces.Hidden, StringComparison.OrdinalIgnoreCase);
}
