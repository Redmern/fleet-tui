using Fleet.Features.Dashboard.ShowDashboard;
using Fleet.Ui.Constants;
using Fleet.Features.Dashboard.ShowDashboard.Models;

namespace Fleet.Tests.Features.Dashboard;

public class DashboardRowsTests
{
    [Fact]
    public void An_empty_list_becomes_a_single_hint_row()
    {
        var rows = DashboardRows.ForRepositories([]);

        Assert.Equal(DashboardRows.EmptyHint, Assert.Single(rows).Text);
    }

    [Fact]
    public void Each_repository_becomes_a_row_with_its_default_branch()
    {
        var rows = DashboardRows.ForRepositories([("widgets", "main"), ("api", "develop")]);

        Assert.Equal(
            [
                $"widgets   {FleetGlyphs.Branch} main",
                $"api       {FleetGlyphs.Branch} develop",
            ],
            rows.Select(r => r.Text));
    }

    [Fact]
    public void Row_order_follows_the_input_order()
    {
        var rows = DashboardRows.ForRepositories([("zeta", "main"), ("alpha", "main")]);

        Assert.StartsWith("zeta", rows[0].Text);
    }
}
