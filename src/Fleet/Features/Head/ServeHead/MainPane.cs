using Fleet.Ports.Mux.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;

namespace Fleet.Features.Head.ServeHead;

public static class MainPane
{
    public static Pane? Find(IReadOnlyList<Pane> panes, string root, string? dashPane) =>
        Candidates(panes, dashPane)
            .Where(p => PathKey.Same(p.Cwd, root))
            .OrderBy(p => InDashboardTab(p) ? 0 : 1)
            .FirstOrDefault();

    public static Pane? Find(IReadOnlyList<Pane> panes, string root, string project, string? dashPane) =>
        Find(panes, root, dashPane) ?? Adopted(panes, project, dashPane);

    public static Pane? Dashboard(IReadOnlyList<Pane> panes, string? dashPane) =>
        dashPane is null ? null : panes.FirstOrDefault(p => p.Id.Value == dashPane);

    private static Pane? Adopted(IReadOnlyList<Pane> panes, string project, string? dashPane)
    {
        var tab = panes
            .Where(p => string.Equals(p.SessionName, project, StringComparison.OrdinalIgnoreCase) && InDashboardTab(p))
            .ToList();

        return dashPane is not null && tab.Count >= 2 && tab.Any(p => p.Id.Value == dashPane)
            ? Candidates(tab, dashPane).FirstOrDefault()
            : null;
    }

    private static IEnumerable<Pane> Candidates(IEnumerable<Pane> panes, string? dashPane) =>
        panes.Where(p => p.Id.Value != dashPane
            && !FleetWorkspaces.IsHidden(p.SessionName)
            && !string.Equals(p.SessionName, FleetWorkspaces.Head, StringComparison.OrdinalIgnoreCase));

    private static bool InDashboardTab(Pane pane) =>
        string.Equals(pane.Title, FleetTabTitles.Dashboard, StringComparison.OrdinalIgnoreCase);
}
