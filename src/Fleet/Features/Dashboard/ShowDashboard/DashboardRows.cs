using Fleet.Features.Dashboard.ShowDashboard.Models;

namespace Fleet.Features.Dashboard.ShowDashboard;

public static class DashboardRows
{
    public const string EmptyHint = "(no repositories - add one from the fleet menu)";

    public static IReadOnlyList<DashboardRow> ForRepositories(
        IReadOnlyList<(string Name, string DefaultBranch)> repositories)
        => repositories.Count == 0
            ? [new DashboardRow(EmptyHint)]
            : repositories
                .Select(r => new DashboardRow($"{r.Name}   [{r.DefaultBranch}]"))
                .ToList();
}
