using Fleet.Features.Dashboard.ShowDashboard;
using Fleet.Features.Dashboard.ShowDashboard.Enums;
using Fleet.Ui;
using Terminal.Gui.Input;

namespace Fleet.Tests.Features.Dashboard;

public class DashboardKeysTests
{
    [Fact]
    public void Escape_never_closes_the_dashboard()
        => Assert.Equal(DashboardAction.None, DashboardKeys.For(Key.Esc));

    [Fact]
    public void Only_an_explicit_quit_keybind_closes_the_dashboard()
    {
        Assert.Equal(DashboardAction.Quit, DashboardKeys.For(FleetKeys.Quit));
        Assert.Equal(DashboardAction.None, DashboardKeys.For(FleetKeys.Cancel));
    }

    [Fact]
    public void Add_and_refresh_map_to_their_actions()
    {
        Assert.Equal(DashboardAction.Add, DashboardKeys.For(FleetKeys.Add));
        Assert.Equal(DashboardAction.Refresh, DashboardKeys.For(FleetKeys.Refresh));
    }

    [Theory]
    [InlineData("J")]
    [InlineData("K")]
    [InlineData("G")]
    [InlineData("Tab")]
    [InlineData("Enter")]
    public void Motion_and_focus_keys_are_left_to_the_widget(string keyName)
    {
        var key = (Key)typeof(Key).GetProperty(keyName)!.GetValue(null)!;

        Assert.Equal(DashboardAction.None, DashboardKeys.For(key));
    }
}
