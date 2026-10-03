using Fleet.Ports.Mux.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;

namespace Fleet.Features.Head.ServeHead;

public static class MainPane
{
    public static Pane? Find(IReadOnlyList<Pane> panes, string root, string? dashPane) =>
        panes
            .Where(p => PathKey.Same(p.Cwd, root)
                && p.Id.Value != dashPane
                && !FleetWorkspaces.IsHidden(p.SessionName)
                && !string.Equals(p.SessionName, FleetWorkspaces.Head, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => string.Equals(p.Title, FleetTabTitles.Dashboard, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .FirstOrDefault();

    public static Pane? Dashboard(IReadOnlyList<Pane> panes, string? dashPane) =>
        dashPane is null ? null : panes.FirstOrDefault(p => p.Id.Value == dashPane);
}
