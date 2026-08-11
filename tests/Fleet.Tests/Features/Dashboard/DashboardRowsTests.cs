using Fleet.Features.Dashboard.ShowDashboard;
using Fleet.Features.Dashboard.ShowDashboard.Models;
using Fleet.Shared;
using Fleet.Ui.Constants;

namespace Fleet.Tests.Features.Dashboard;

public class DashboardRowsTests
{
    private static RepositoryChoice Repo(string name, string branch = "develop") =>
        new(name, $"C:/repos/techweb/{name}", branch);

    [Fact]
    public void An_empty_list_becomes_a_single_hint_row()
    {
        var rows = DashboardRows.ForRepositories([], _ => BranchState.Unknown);

        Assert.Equal([DashboardRows.EmptyHint], rows.Select(r => r.Text));
    }

    [Fact]
    public void Each_repository_shows_its_name_branch_and_status()
    {
        var rows = DashboardRows.ForRepositories(
            [Repo("backend"), Repo("frontend")], _ => new BranchState(0, 2, false));

        Assert.All(rows, r => Assert.Contains(FleetGlyphs.Branch, r.Text));
        Assert.All(rows, r => Assert.Contains($"{FleetGlyphs.Behind}2", r.Text));
        Assert.Contains("backend", rows[0].Text);
        Assert.Contains("develop", rows[0].Text);
    }

    [Fact]
    public void Row_order_follows_the_input_order()
    {
        var rows = DashboardRows.ForRepositories(
            [Repo("zeta"), Repo("alpha")], _ => BranchState.Unknown);

        Assert.EndsWith("zeta", rows[0].Text);
        Assert.EndsWith("alpha", rows[1].Text);
    }

    [Fact]
    public void The_branch_pill_comes_first_and_the_repository_name_follows_it()
    {
        var rows = DashboardRows.ForRepositories(
            [Repo("backend", "develop")], _ => BranchState.Unknown);

        var branch = rows[0].Text.IndexOf("develop", StringComparison.Ordinal);
        var name = rows[0].Text.IndexOf("backend", StringComparison.Ordinal);

        Assert.True(branch < name, "the pill opens the row");
    }

    [Fact]
    public void A_repository_level_with_its_upstream_shows_no_counts()
    {
        var rows = DashboardRows.ForRepositories([Repo("backend")], _ => BranchState.Unknown);

        Assert.DoesNotContain(FleetGlyphs.Ahead, rows[0].Text);
        Assert.DoesNotContain(FleetGlyphs.Behind, rows[0].Text);
    }
}
