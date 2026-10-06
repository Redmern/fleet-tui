using Fleet.Features.Menu.ShowMenu;
using Fleet.Features.Menu.ShowMenu.Models;
using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Settings.Models;
using Fleet.Ui;
using Fleet.Ui.Constants;
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

        Assert.Equal([0, 2, 5], headers.Keys.Order());
        Assert.Equal(FleetMenus.Actions(FleetMenus.Settings), items.Select(i => i.Action));
    }

    // Headers are a quiet caption in line with the icons, without the old ── rules.
    [Fact]
    public void A_header_is_a_muted_caption_lined_up_with_the_icon_column()
    {
        var items = new ShowMenuHandler(Keymap.Default).Items(FleetMenus.Settings, _ => null);
        var rows = ShowMenuHandler.Rows(items);
        var header = ShowMenuHandler.Headers(items)[2];

        Assert.Equal(FleetTones.Muted, Assert.Single(header.Spans).Tone);
        Assert.DoesNotContain("─", header.Text);
        Assert.Equal($"{new string(' ', rows[2].Spans[0].Text.Length)}{FleetIcons.Configure}  configure", header.Text);
    }

    [Fact]
    public void Without_keys_a_header_starts_at_the_left_edge_like_the_rows()
    {
        var items = new ShowMenuHandler(Keymap.Default).Items(FleetMenus.Settings, _ => null);

        Assert.Equal($"{FleetIcons.Session}  session", ShowMenuHandler.Headers(items, keys: false)[0].Text);
    }

    [Fact]
    public void A_section_without_an_icon_keeps_a_plain_header()
    {
        var items = new ShowMenuHandler(Keymap.Default).Items([new MenuSection("plain", [FleetAction.ViewLogs])], _ => null);

        Assert.Equal("plain", ShowMenuHandler.Headers(items, keys: false)[0].Text);
    }

    [Fact]
    public void A_blank_row_separates_each_section_from_the_one_above()
    {
        var items = new ShowMenuHandler(Keymap.Default).Items(FleetMenus.Settings, _ => null);
        var gaps = ShowMenuHandler.Gaps(items);
        var source = new FleetRowSource(
            ShowMenuHandler.Rows(items), gaps, headersBefore: ShowMenuHandler.Headers(items));

        Assert.Equal([1, 4], gaps);
        Assert.Equal(items.Count + 3 + 2, source.Count);
        Assert.Equal(
            ShowMenuHandler.Height(ShowMenuHandler.Rows(items), 3, gaps.Count),
            source.Count);
        Assert.Equal(string.Empty, source.ToList()[3]);
        Assert.Equal(string.Empty, source.ToList()[8]);
    }

    [Fact]
    public void A_menu_with_one_section_has_no_blank_rows()
        => Assert.Empty(ShowMenuHandler.Gaps(new ShowMenuHandler(Keymap.Default).Items(FleetMenus.Main, _ => null)));

    [Theory]
    [InlineData(FleetAction.QuitFleet, "Quit")]
    [InlineData(FleetAction.BrowseFiles, "Files")]
    [InlineData(FleetAction.SwitchProject, "Switch")]
    [InlineData(FleetAction.FocusMain, "Dashboard")]
    [InlineData(FleetAction.SaveSession, "Save session")]
    [InlineData(FleetAction.RebuildDashboard, "Rebuild dashboard")]
    [InlineData(FleetAction.CleanupProject, "Clean up agents")]
    [InlineData(FleetAction.EditShowMenuKeys, "Show keybinds")]
    [InlineData(FleetAction.ListAgents, "List agents")]
    public void Menu_rows_use_the_short_labels(FleetAction action, string label)
    {
        var sections = FleetMenus.Main.Concat(FleetMenus.Settings).ToList();
        var items = new ShowMenuHandler(Keymap.Default).Items(sections, _ => null);

        Assert.Equal(label, items.Single(i => i.Action == action).Label);
    }

    [Fact]
    public void Show_keybinds_is_a_toggle_in_settings_that_defaults_to_on()
    {
        Assert.Contains(FleetAction.EditShowMenuKeys, FleetMenus.Actions(FleetMenus.Settings));
        Assert.True(FleetMenus.IsToggle(FleetAction.EditShowMenuKeys));
        Assert.True(SettingsConfig.Default.ShowMenuKeys);
        Assert.Equal("[on]", FleetMenus.Value(FleetAction.EditShowMenuKeys, SettingsConfig.Default));

        var off = FleetMenus.Flip(FleetAction.EditShowMenuKeys, SettingsConfig.Default);

        Assert.False(off.ShowMenuKeys);
        Assert.Equal("[off]", FleetMenus.Value(FleetAction.EditShowMenuKeys, off));
        Assert.NotEqual(SettingsConfig.Default.Signature, off.Signature);
        Assert.False(off.MergedOverDefaults().ShowMenuKeys);
    }

    [Fact]
    public void Without_keys_a_row_starts_with_its_icon()
    {
        var items = new ShowMenuHandler(Keymap.Default).Items(FleetMenus.Main, _ => null);
        var rows = ShowMenuHandler.Rows(items, keys: false);

        Assert.All(rows, r => Assert.NotEqual(FleetTones.Key, r.Spans[0].Tone));
        Assert.Equal($"{FleetIcons.For(FleetAction.QuitFleet)}  Quit", rows[0].Text);
        Assert.True(ShowMenuHandler.Width(rows) < ShowMenuHandler.Width(ShowMenuHandler.Rows(items)));
    }

    // The reveal key works in every menu, so it may not be any menu row's key or a motion.
    [Fact]
    public void The_reveal_key_is_question_mark_and_collides_with_no_menu_or_motion_key()
    {
        var reveal = KeymapDefaults.Bindings[FleetAction.RevealMenuKeys];
        FleetAction[] motions =
        [
            FleetAction.MoveDown, FleetAction.MoveUp, FleetAction.MoveFirst, FleetAction.MoveLast,
            FleetAction.PageDown, FleetAction.PageUp, FleetAction.Close,
        ];
        var used = FleetMenus.Actions([.. FleetMenus.Main, .. FleetMenus.Settings, .. FleetMenus.FleetConfig])
            .Concat(motions)
            .Select(a => KeymapDefaults.Bindings[a]);

        Assert.Equal("?", reveal);
        Assert.DoesNotContain(reveal, used);
        Assert.Equal(new Terminal.Gui.Input.Key('?'), Keymap.Default.KeyFor(FleetAction.RevealMenuKeys));
    }

    [Fact]
    public void Every_entry_and_section_in_the_fleet_menus_has_a_single_cell_nerd_font_icon()
    {
        var sections = FleetMenus.Main.Concat(FleetMenus.Settings).Concat(FleetMenus.FleetConfig).ToList();

        Assert.All(FleetMenus.Actions(sections), a =>
        {
            var icon = FleetIcons.For(a);
            Assert.NotNull(icon);
            Assert.InRange(Assert.Single(icon), '', '');
        });
        Assert.All(sections.Where(s => s.Header is not null), s => Assert.InRange(Assert.Single(s.Icon!), '', ''));
    }

    [Fact]
    public void A_row_shows_the_icon_then_a_gap_then_the_label()
    {
        var items = new ShowMenuHandler(Keymap.Default).Items(FleetMenus.Main, _ => null);
        var rows = ShowMenuHandler.Rows(items);
        var settings = rows[items.ToList().FindIndex(i => i.Action == FleetAction.OpenSettings)];

        Assert.Equal($"{FleetIcons.For(FleetAction.OpenSettings)}  ", settings.Spans[1].Text);
        Assert.Equal(items.Single(i => i.Action == FleetAction.OpenSettings).Label, settings.Spans[2].Text);
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

    [Theory]
    [InlineData(FleetAction.EditKeybinds)]
    [InlineData(FleetAction.ViewLogs)]
    [InlineData(FleetAction.OpenFleetConfigMenu)]
    public void Settings_entries_that_open_a_screen_end_in_a_marker(FleetAction action)
    {
        var items = new ShowMenuHandler(Keymap.Default).Items(FleetMenus.Settings, _ => null);
        var rows = ShowMenuHandler.Rows(items);

        Assert.Equal("›", Assert.Single(rows[items.ToList().FindIndex(i => i.Action == action)].Trailing!).Text);
    }

    [Fact]
    public void An_empty_section_brings_no_header()
    {
        var items = new ShowMenuHandler(Keymap.Default).Items(
            [new MenuSection("gone", []), new MenuSection("kept", [FleetAction.ViewLogs])], _ => null);

        Assert.Equal("kept", Assert.Single(items).Header);
    }
}
