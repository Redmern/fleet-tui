using Fleet.Features.Dashboard.ShowDashboard;

namespace Fleet.Tests.Features.Dashboard;

public class DashboardTabsTests
{
    [Fact]
    public void Agents_is_the_first_tab_so_it_is_the_one_showing_on_open()
    {
        Assert.Equal(0, DashboardTabs.AgentsTab);
        Assert.Equal(1, DashboardTabs.SubsTab);
        Assert.Equal(2, DashboardTabs.RepositoriesTab);
    }

    [Fact]
    public void A_tab_title_carries_its_count_because_the_other_tabs_are_hidden()
    {
        Assert.Equal("Repositories (2)", DashboardTabs.Repositories(2));
        Assert.Equal("Subs (3)", DashboardTabs.Subs(3));
        Assert.Equal("Agents (0)", DashboardTabs.Agents(0));
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(1, 1, 2)]
    [InlineData(2, -1, 1)]
    public void Stepping_walks_all_three_tabs(int current, int delta, int expected)
    {
        Assert.Equal(expected, DashboardTabs.Step(current, delta, 3));
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(1, -1, 0)]
    public void Stepping_moves_one_tab_in_the_direction_asked(
        int current, int delta, int expected)
    {
        Assert.Equal(expected, DashboardTabs.Step(current, delta, 2));
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(0, -1, 0)]
    public void Stepping_past_an_end_stays_put_so_h_and_l_mean_left_and_right(
        int current, int delta, int expected)
    {
        Assert.Equal(expected, DashboardTabs.Step(current, delta, 2));
    }

    [Fact]
    public void Wrapping_would_make_h_and_l_identical_with_only_two_tabs()
    {
        Assert.NotEqual(DashboardTabs.Step(0, -1, 2), DashboardTabs.Step(0, 1, 2));
        Assert.NotEqual(DashboardTabs.Step(1, -1, 2), DashboardTabs.Step(1, 1, 2));
    }

    [Fact]
    public void Stepping_with_no_tabs_stays_put_rather_than_dividing_by_zero()
    {
        Assert.Equal(0, DashboardTabs.Step(0, 1, 0));
    }
}
