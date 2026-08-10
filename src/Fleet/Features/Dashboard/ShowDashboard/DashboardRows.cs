using Fleet.Features.Dashboard.ShowDashboard.Models;
using Fleet.Ui;

using Fleet.Shared;

namespace Fleet.Features.Dashboard.ShowDashboard;

public static class DashboardRows
{
    public const string EmptyHint = "(no repositories - add one from the fleet menu)";

    public static IReadOnlyList<DashboardRow> ForRepositories(
        IReadOnlyList<RepositoryChoice> repositories, Func<RepositoryChoice, BranchState> state)
    {
        if (repositories.Count == 0)
        {
            return [new DashboardRow(EmptyHint)];
        }

        var nameWidth = repositories.Max(r => r.Name.Length);
        var pills = repositories.Select(r => BranchStatus.Pill(r.DefaultBranch)).ToList();
        var pillWidth = pills.Max(p => p.Length);

        return
        [
            .. repositories.Select((r, i) => new DashboardRow(
                $"{r.Name.PadRight(nameWidth)}   {pills[i].PadRight(pillWidth)}   " +
                BranchStatus.Of(state(r)))),
        ];
    }
}
