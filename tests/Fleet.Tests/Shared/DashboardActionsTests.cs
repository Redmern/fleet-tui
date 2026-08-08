using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;

namespace Fleet.Tests.Shared;

public class DashboardActionsTests
{
    [Theory]
    [InlineData(FleetAction.AddRepository)]
    [InlineData(FleetAction.EditKeybinds)]
    [InlineData(FleetAction.Refresh)]
    public void The_dashboard_renders_these_itself(FleetAction action)
    {
        Assert.True(DashboardActions.IsServed(action));
    }

    [Theory]
    [InlineData(FleetAction.OpenProject)]
    [InlineData(FleetAction.NewProject)]
    [InlineData(FleetAction.None)]
    public void These_need_a_pane_of_their_own(FleetAction action)
    {
        Assert.False(DashboardActions.IsServed(action));
    }
}
