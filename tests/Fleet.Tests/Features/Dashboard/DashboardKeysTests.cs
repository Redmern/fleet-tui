using Fleet.Features.Dashboard.ShowDashboard;
using Fleet.Shared.Keymap.Enums;
using Fleet.Ui;
using Terminal.Gui.Input;

namespace Fleet.Tests.Features.Dashboard;

public class DashboardKeysTests
{
    private static Keymap Map => Keymap.Default;

    [Fact]
    public void Escape_is_swallowed_so_it_can_never_close_the_pane()
    {
        var result = DashboardKeys.For(Key.Esc, Map);

        Assert.True(result.Consume);
        Assert.Equal(FleetAction.None, result.Action);
    }

    [Fact]
    public void Only_the_close_keybind_closes_the_pane()
    {
        Assert.Equal(FleetAction.Close, DashboardKeys.For(Map.KeyFor(FleetAction.Close), Map).Action);
        Assert.Equal(FleetAction.None, DashboardKeys.For(Key.Esc, Map).Action);
    }

    [Fact]
    public void Add_and_refresh_map_to_their_actions()
    {
        Assert.Equal(
            FleetAction.AddRepository,
            DashboardKeys.For(Map.KeyFor(FleetAction.AddRepository), Map).Action);

        Assert.Equal(
            FleetAction.Refresh,
            DashboardKeys.For(Map.KeyFor(FleetAction.Refresh), Map).Action);
    }

    [Theory]
    [InlineData("J")]
    [InlineData("K")]
    [InlineData("Tab")]
    [InlineData("Enter")]
    [InlineData("CursorDown")]
    public void Motion_and_focus_keys_are_left_to_the_widget(string keyName)
    {
        var key = (Key)typeof(Key).GetProperty(keyName)!.GetValue(null)!;

        var result = DashboardKeys.For(key, Map);

        Assert.False(result.Consume);
        Assert.Equal(FleetAction.None, result.Action);
    }

    [Fact]
    public void The_keybinds_key_does_not_fire_bare_because_it_collides_with_move_up()
    {
        Assert.Equal(Map.KeyFor(FleetAction.MoveUp), Map.KeyFor(FleetAction.EditKeybinds));

        var result = DashboardKeys.For(Map.KeyFor(FleetAction.EditKeybinds), Map);

        Assert.False(result.Consume);
        Assert.Equal(FleetAction.None, result.Action);
    }

    [Fact]
    public void The_menu_key_does_not_fire_bare_because_it_belongs_to_the_prefix()
    {
        var result = DashboardKeys.For(Map.KeyFor(FleetAction.OpenMenu), Map);

        Assert.False(result.Consume);
    }

    [Fact]
    public void A_rebound_close_key_is_what_closes_the_pane()
    {
        var keymap = new Keymap(
            global::Fleet.Shared.Keymap.Models.KeymapConfig.Default.With(FleetAction.Close, "x"));

        Assert.Equal(FleetAction.Close, DashboardKeys.For(Key.X, keymap).Action);
        Assert.True(DashboardKeys.For(Key.Esc, keymap).Consume);
        Assert.Equal(FleetAction.None, DashboardKeys.For(Key.Esc, keymap).Action);
    }
}
