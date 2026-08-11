using Fleet.Features.Dashboard.ShowDashboard.Models;
using Fleet.Shared;
using Fleet.Ui;
using Fleet.Ui.Models;

namespace Fleet.Features.Dashboard.ShowDashboard;

public static class DashboardRows
{
    public const string EmptyHint = "(no repositories - add one from the fleet menu)";

    public static IReadOnlyList<FleetRow> ForRepositories(
        IReadOnlyList<RepositoryChoice> repositories, Func<RepositoryChoice, BranchState> state)
    {
        if (repositories.Count == 0)
        {
            return [FleetRow.Plain(EmptyHint)];
        }

        var pills = repositories
            .Select(r => BranchStatus.Pill(r.DefaultBranch, state(r)))
            .ToList();

        var pillWidth = pills.Max(p => p.Sum(s => s.Text.Length));

        return
        [
            .. repositories.Select((r, i) => new FleetRow(
            [
                .. pills[i],
                FleetSpan.Plain(new string(' ', pillWidth - pills[i].Sum(s => s.Text.Length) + 3)),
                FleetSpan.Muted(r.Name),
            ])),
        ];
    }
}
