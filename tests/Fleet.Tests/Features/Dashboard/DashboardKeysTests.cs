using Fleet.Features.Dashboard.ShowDashboard;
using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Models;
using Fleet.Shared.Keymap.Enums;
using Fleet.Ui;
using Terminal.Gui.Input;

namespace Fleet.Tests.Features.Dashboard;

public class DashboardKeysTests
{
    private static Keymap Map => Keymap.Default;

    private const int Agents = DashboardTabs.AgentsTab;

    private const int Repositories = DashboardTabs.RepositoriesTab;

    [Fact]
    public void Escape_is_swallowed_so_it_can_never_close_the_pane()
    {
        var result = DashboardKeys.For(Key.Esc, Map, Agents);

        Assert.True(result.Consume);
        Assert.Equal(FleetAction.None, result.Action);
    }

    [Fact]
    public void No_bare_key_closes_the_dashboard_because_it_is_the_main_pane()
    {
        Assert.False(DashboardKeys.For(Map.KeyFor(FleetAction.Close), Map, Agents).Consume);
        Assert.Equal(FleetAction.None, DashboardKeys.For(Key.Esc, Map, Agents).Action);
    }

    [Fact]
    public void Closing_is_still_reachable_through_the_fleet_menu()
    {
        Assert.Contains(FleetAction.Close, DashboardActions.Served);
        Assert.Equal(FleetAction.Close, FleetActionIds.Parse(FleetActionIds.For(FleetAction.Close)));
    }

    [Fact]
    public void Refresh_maps_to_its_action()
    {
        Assert.Equal(
            FleetAction.Refresh,
            DashboardKeys.For(Map.KeyFor(FleetAction.Refresh), Map, Agents).Action);
    }

    [Fact]
    public void The_new_key_means_agent_or_repository_depending_on_the_tab()
    {
        Assert.Equal(FleetAction.NewAgent, DashboardKeys.For(Key.N, Map, Agents).Action);
        Assert.Equal(
            FleetAction.AddRepository, DashboardKeys.For(Key.N, Map, Repositories).Action);
    }

    [Fact]
    public void Managing_an_agent_and_removing_a_repository_have_their_own_keys()
    {
        Assert.Equal(FleetAction.RemoveAgent, DashboardKeys.For(Key.M, Map, Agents).Action);
        Assert.Equal(
            FleetAction.RemoveRepository, DashboardKeys.For(Key.D, Map, Repositories).Action);
    }

    [Fact]
    public void Removing_a_repository_never_fires_on_the_agents_tab()
    {
        Assert.False(DashboardKeys.For(Key.D, Map, Agents).Consume);
    }

    [Fact]
    public void The_harness_and_hide_keys_are_gone_because_they_live_in_the_manage_menu()
    {
        Assert.Equal(Key.Empty, Map.KeyFor(FleetAction.ChangeHarness));
        Assert.Equal(Key.Empty, Map.KeyFor(FleetAction.ToggleHidden));

        Assert.False(DashboardKeys.For(Key.C, Map, Agents).Consume);
        Assert.False(DashboardKeys.For(Key.X, Map, Agents).Consume);
    }

    [Fact]
    public void Stopping_has_no_bare_key_because_it_lives_in_the_manage_menu()
    {
        Assert.Equal(Key.Empty, Map.KeyFor(FleetAction.StopAgent));
        Assert.DoesNotContain(FleetAction.StopAgent, DashboardKeys.ScopeFor(Agents));
    }

    [Fact]
    public void Tabs_can_still_be_switched_from_either_side()
    {
        Assert.Equal(FleetAction.PrevTab, DashboardKeys.For(Key.H, Map, Repositories).Action);
        Assert.Equal(FleetAction.NextTab, DashboardKeys.For(Key.L, Map, Repositories).Action);
    }

    [Fact]
    public void The_new_agent_key_starts_an_agent()
    {
        Assert.Equal(Key.N, Map.KeyFor(FleetAction.NewAgent));
        Assert.Equal(FleetAction.NewAgent, DashboardKeys.For(Key.N, Map, Agents).Action);
    }

    [Fact]
    public void A_keymap_saved_before_agents_existed_still_gets_the_new_agent_key()
    {
        var saved = new global::Fleet.Shared.Keymap.Models.KeymapConfig(
            KeymapDefaults.Prefix,
            new Dictionary<FleetAction, string>
            {
                [FleetAction.Close] = "q",
                [FleetAction.Refresh] = "r",
            });

        var keymap = new Keymap(saved);

        Assert.Equal(FleetAction.NewAgent, DashboardKeys.For(Key.N, keymap, Agents).Action);
    }

    [Fact]
    public void Tabs_are_switched_with_h_and_l()
    {
        Assert.Equal(FleetAction.PrevTab, DashboardKeys.For(Key.H, Map, Agents).Action);
        Assert.Equal(FleetAction.NextTab, DashboardKeys.For(Key.L, Map, Agents).Action);
    }

    [Fact]
    public void The_arrow_keys_switch_tabs_too()
    {
        Assert.Equal(FleetAction.PrevTab, DashboardKeys.For(Key.CursorLeft, Map, Agents).Action);
        Assert.Equal(FleetAction.NextTab, DashboardKeys.For(Key.CursorRight, Map, Agents).Action);
    }

    [Fact]
    public void The_next_tab_key_wins_over_open_project_which_shares_it()
    {
        Assert.Equal(Map.KeyFor(FleetAction.OpenProject), Map.KeyFor(FleetAction.NextTab));

        Assert.Equal(FleetAction.NextTab, DashboardKeys.For(Key.L, Map, Agents).Action);
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

        var result = DashboardKeys.For(key, Map, Agents);

        Assert.False(result.Consume);
        Assert.Equal(FleetAction.None, result.Action);
    }

    [Fact]
    public void The_keybinds_key_does_not_fire_bare_because_it_collides_with_move_up()
    {
        Assert.Equal(Map.KeyFor(FleetAction.MoveUp), Map.KeyFor(FleetAction.EditKeybinds));

        var result = DashboardKeys.For(Map.KeyFor(FleetAction.EditKeybinds), Map, Agents);

        Assert.False(result.Consume);
        Assert.Equal(FleetAction.None, result.Action);
    }

    [Fact]
    public void The_menu_key_does_not_fire_bare_because_it_belongs_to_the_prefix()
    {
        var result = DashboardKeys.For(Map.KeyFor(FleetAction.OpenMenu), Map, Agents);

        Assert.False(result.Consume);
    }

    [Fact]
    public void A_rebound_refresh_key_is_what_refreshes()
    {
        var keymap = new Keymap(
            global::Fleet.Shared.Keymap.Models.KeymapConfig.Default.With(FleetAction.Refresh, "F5"));

        Assert.Equal(
            FleetAction.Refresh, DashboardKeys.For(new Key("F5"), keymap, Agents).Action);
        Assert.True(DashboardKeys.For(Key.Esc, keymap, Agents).Consume);
        Assert.Equal(FleetAction.None, DashboardKeys.For(Key.Esc, keymap, Agents).Action);
    }
}
