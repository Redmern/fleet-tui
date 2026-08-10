using Fleet.Features.Dashboard.ShowDashboard.Models;
using Fleet.Ui.Constants;

namespace Fleet.Features.Dashboard.ShowDashboard;

public static class DashboardRows
{
    public const string EmptyHint = "(no repositories - add one from the fleet menu)";

    public static IReadOnlyList<DashboardRow> ForRepositories(
        IReadOnlyList<(string Name, string DefaultBranch)> repositories)
    {
        if (repositories.Count == 0)
        {
            return [new DashboardRow(EmptyHint)];
        }

        var width = repositories.Max(r => r.Name.Length);

        return repositories
            .Select(r => new DashboardRow(
                $"{r.Name.PadRight(width)}   {FleetGlyphs.Branch} {r.DefaultBranch}"))
            .ToList();
    }
}
