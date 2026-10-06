using Fleet.Features.Menu.ShowMenu;
using Fleet.Features.Menu.ShowMenu.Models;
using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Settings.Models;
using Fleet.Ui;
using Fleet.Ui.Models;

namespace Fleet.Tests.Features.Menu;

public class FleetMenusTests
{
    public static TheoryData<FleetAction> Menus => new()
    {
        FleetAction.None,
        FleetAction.OpenSettings,
        FleetAction.OpenFleetConfigMenu,
    };

    [Theory]
    [MemberData(nameof(Menus))]
    public void Keys_are_unique_within_each_menu(FleetAction submenu)
    {
        var keys = FleetMenus.Actions(FleetMenus.For(submenu))
            .Select(a => KeymapDefaults.Bindings[a])
            .ToList();

        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Every_submenu_is_reachable_from_its_parent()
    {
        Assert.Contains(FleetAction.OpenSettings, FleetMenus.Actions(FleetMenus.Main));
        Assert.Contains(FleetAction.OpenFleetConfigMenu, FleetMenus.Actions(FleetMenus.Settings));
        Assert.True(FleetMenus.IsSubmenu(FleetAction.OpenFleetConfigMenu));
        Assert.False(FleetMenus.IsSubmenu(FleetAction.EditSettings));
    }

    [Fact]
    public void Settings_is_split_into_session_configure_and_maintenance()
    {
        Assert.Equal(
            ["session", "configure", "maintenance"],
            FleetMenus.Settings.Select(s => s.Header));
        Assert.Equal("x", KeymapDefaults.Bindings[FleetAction.CleanupProject]);
        Assert.Equal("c", KeymapDefaults.Bindings[FleetAction.OpenFleetConfigMenu]);
    }

    [Fact]
    public void Permissions_sits_last_in_fleet_config_under_its_own_header()
    {
        var last = FleetMenus.FleetConfig[^1];

        Assert.Equal("permissions", last.Header);
        Assert.Equal([FleetAction.EditSettings], last.Actions);
    }

    // The keybinds screen groups by menu, so each menu's group lists the same
    // actions. Open editor is the exception: it is a dashboard key first.
    [Theory]
    [InlineData(FleetAction.None, "fleet menu")]
    [InlineData(FleetAction.OpenSettings, "fleet menu › settings")]
    [InlineData(FleetAction.OpenFleetConfigMenu, "fleet menu › settings › fleet config")]
    public void Each_menu_has_a_matching_keybinds_group(FleetAction submenu, string label)
    {
        var group = KeymapGroups.All.Single(g => g.Label == label);

        Assert.Equal(label, FleetMenus.Title(submenu));
        Assert.Equal(
            FleetMenus.Actions(FleetMenus.For(submenu)).Where(a => a != FleetAction.OpenEditor),
            group.Actions);
    }

    [Fact]
    public void Open_editor_can_be_left_out_of_the_main_menu()
    {
        var without = FleetMenus.Actions(FleetMenus.Without(FleetMenus.Main, FleetAction.OpenEditor));

        Assert.Contains(FleetAction.OpenEditor, FleetMenus.Actions(FleetMenus.Main));
        Assert.DoesNotContain(FleetAction.OpenEditor, without);
        Assert.Equal(FleetMenus.Actions(FleetMenus.Main).Count - 1, without.Count);
    }

    [Fact]
    public void A_toggle_shows_its_state_and_flipping_it_changes_only_that_setting()
    {
        var off = SettingsConfig.Default
            .WithMainOrchestratorInNvim(false)
            .WithSubOrchestratorsInNvim(false);

        Assert.Equal("[off]", FleetMenus.Value(FleetAction.EditMainOrchestratorInNvim, off));

        var on = FleetMenus.Flip(FleetAction.EditMainOrchestratorInNvim, off);

        Assert.Equal("[on]", FleetMenus.Value(FleetAction.EditMainOrchestratorInNvim, on));
        Assert.Equal("[off]", FleetMenus.Value(FleetAction.EditSubOrchestratorsInNvim, on));
        Assert.Equal(off, FleetMenus.Flip(FleetAction.EditMainOrchestratorInNvim, on));
    }

    [Fact]
    public void Auto_close_shows_its_minutes_but_is_not_a_toggle()
    {
        Assert.Equal("[off]", FleetMenus.Value(FleetAction.EditAutoClose, SettingsConfig.Default.WithAutoClose(false, 30)));
        Assert.Equal("[30m]", FleetMenus.Value(FleetAction.EditAutoClose, SettingsConfig.Default.WithAutoClose(true, 30)));
        Assert.False(FleetMenus.IsToggle(FleetAction.EditAutoClose));
        Assert.Null(FleetMenus.Value(FleetAction.EditKeybinds, SettingsConfig.Default));
    }

    [Fact]
    public void Items_carry_the_section_header_on_the_first_item_of_each_section()
    {
        var items = new ShowMenuHandler(Keymap.Default).Items(FleetMenus.Settings, _ => null);
        var headers = ShowMenuHandler.Headers(items);

        Assert.Equal([0, 2, 4], headers.Keys.Order());
        Assert.Equal("── configure ──", headers[2].Text);
        Assert.Equal(FleetMenus.Actions(FleetMenus.Settings), items.Select(i => i.Action));
    }

    [Fact]
    public void Toggle_rows_end_in_their_value_and_submenu_rows_in_a_marker()
    {
        var current = SettingsConfig.Default.WithMainOrchestratorInNvim(true);
        var items = new ShowMenuHandler(Keymap.Default)
            .Items(FleetMenus.FleetConfig, a => FleetMenus.Value(a, current));
        var rows = ShowMenuHandler.Rows(items);

        FleetRow RowFor(FleetAction action) =>
            rows[items.ToList().FindIndex(i => i.Action == action)];

        Assert.True(items.Single(i => i.Action == FleetAction.EditMainOrchestratorInNvim).Toggles);
        Assert.Equal("[on]", Assert.Single(RowFor(FleetAction.EditMainOrchestratorInNvim).Trailing!).Text);
        Assert.Equal("›", Assert.Single(RowFor(FleetAction.EditSettings).Trailing!).Text);
        Assert.Null(RowFor(FleetAction.EditFleetConfig).Trailing);
    }

    [Fact]
    public void An_empty_section_brings_no_header()
    {
        var items = new ShowMenuHandler(Keymap.Default).Items(
            [new MenuSection("gone", []), new MenuSection("kept", [FleetAction.ViewLogs])], _ => null);

        Assert.Equal("kept", Assert.Single(items).Header);
    }
}
