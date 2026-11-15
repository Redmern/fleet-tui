using Fleet.Features.Menu.ShowMenu;
using Fleet.Features.Menu.ShowMenu.Models;
using Fleet.Shared.Viewing;
using Fleet.Shared.Viewing.Enums;
using Fleet.Shared.Viewing.Models;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Keymap.Models;
using Fleet.Ui;
using Fleet.Ui.Constants;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Fleet.Tests.Features.Menu;

public class ShowMenuTests
{
    private static readonly FleetAction[] DashboardActions =
    [
        FleetAction.AddRepository,
        FleetAction.Refresh,
        FleetAction.EditKeybinds,
        FleetAction.Close,
    ];

    [Fact]
    public void Every_requested_action_becomes_a_menu_item()
    {
        var items = new ShowMenuHandler(Keymap.Default).Items(DashboardActions);

        Assert.Equal(DashboardActions, items.Select(i => i.Action));
    }

    [Fact]
    public void Each_item_shows_its_current_keybind()
    {
        var items = new ShowMenuHandler(Keymap.Default).Items(DashboardActions);

        Assert.Equal("r", items.Single(i => i.Action == FleetAction.Refresh).KeyText);
        Assert.Equal("q", items.Single(i => i.Action == FleetAction.Close).KeyText);
    }

    [Fact]
    public void A_menu_only_action_shows_no_key_because_it_has_none()
    {
        var items = new ShowMenuHandler(Keymap.Default).Items([FleetAction.StopAgent]);

        Assert.Empty(items.Single(i => i.Action == FleetAction.StopAgent).KeyText);
    }

    [Fact]
    public void Each_item_has_a_human_label()
    {
        var items = new ShowMenuHandler(Keymap.Default).Items(DashboardActions);

        Assert.All(items, i => Assert.False(string.IsNullOrWhiteSpace(i.Label)));
        Assert.Equal("Add repository", items[0].Label);
    }

    [Fact]
    public void A_rebound_action_shows_its_new_key_in_the_menu()
    {
        var keymap = new Keymap(KeymapConfig.Default.With(FleetAction.Refresh, "F2"));

        var items = new ShowMenuHandler(keymap).Items(DashboardActions);

        Assert.Equal("f2", items.Single(i => i.Action == FleetAction.Refresh).KeyText);
    }

    [Fact]
    public void Rows_are_aligned_on_the_longest_label()
    {
        var items = new ShowMenuHandler(Keymap.Default).Items(DashboardActions);

        var rows = ShowMenuHandler.Rows(items);

        Assert.Equal(items.Count, rows.Count);

        var labelColumns = rows
            .Select(row => row.Spans[0].Text.Length)
            .Distinct();

        Assert.Single(labelColumns);

        Assert.All(rows, row => Assert.Equal(FleetTones.Key, row.Spans[0].Tone));
    }

    [Fact]
    public void The_dashboard_and_picker_menus_have_an_icon_on_every_row()
    {
        FleetAction[] picker = [FleetAction.NewProject, FleetAction.OpenProject, FleetAction.RemoveProject];
        FleetAction[] dashboard =
        [
            FleetAction.NewAgent, FleetAction.ChangeHarness, FleetAction.ToggleHidden, FleetAction.RemoveAgent,
            FleetAction.RemoveRepository, FleetAction.ViewLogs, FleetAction.BrowseFiles, FleetAction.RebuildDashboard,
        ];

        Assert.All(DashboardActions.Concat(picker).Concat(dashboard), a => Assert.NotNull(FleetIcons.For(a)));
    }

    [Fact]
    public void Rows_without_an_icon_keep_the_labels_aligned()
    {
        var items = new ShowMenuHandler(Keymap.Default).Items([FleetAction.Refresh, FleetAction.MoveDown]);
        var rows = ShowMenuHandler.Rows(items);

        Assert.Equal(rows[0].Spans[1].Text.Length, rows[1].Spans[1].Text.Length);
        Assert.Equal("   ", rows[1].Spans[1].Text);
    }

    // Inside a float the window is exactly the fitted size. The corner padding puts a
    // blank row over the corner buttons and under the bar, and the list sits the same
    // distance under the corners as the bar sits under the list.
    [Theory]
    [InlineData(3)]
    [InlineData(8)]
    [InlineData(13)]
    public void The_space_above_the_first_row_equals_the_space_below_the_last(int height)
    {
        var (list, bar) = ShowMenuView.Place(30, height);
        using var window = new Window { Width = 60, Height = ShowMenuView.FitRows(height), BorderStyle = LineStyle.None };
        window.Add(list, bar.Root);
        FleetCorners.Attach(window, () => { });

        window.Layout();

        var above = list.Frame.Y;
        var below = bar.Root.Frame.Y - list.Frame.Bottom;

        Assert.Equal(ShowMenuView.Header + ShowMenuHandler.Padding, above);
        Assert.Equal(ShowMenuHandler.Padding, below);
        Assert.Equal(window.Viewport.Height - ShowMenuView.Footer, bar.Root.Frame.Y);
        Assert.Equal(window.Frame.Height - FleetCorners.Rows, window.Viewport.Height);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(8)]
    public void A_note_sits_one_row_under_the_last_row_and_the_bar_keeps_its_distance(int height)
    {
        const string note = "Fleet's own screens are in the window on the machine you sit at.";
        var (list, bar) = ShowMenuView.Place(30, height, noted: true);
        var line = ShowMenuView.PlaceNote(list, note);
        using var window = new Window { Width = 80, Height = ShowMenuView.FitRows(height, noted: true), BorderStyle = LineStyle.None };
        window.Add(list, line, bar.Root);
        FleetCorners.Attach(window, () => { });

        window.Layout();

        Assert.Equal(ShowMenuView.FitRows(height) + ShowMenuView.NoteRows, ShowMenuView.FitRows(height, noted: true));
        Assert.Equal(ShowMenuView.Header + ShowMenuHandler.Padding, list.Frame.Y);
        Assert.Equal(list.Frame.Bottom + 1, line.Frame.Y);
        Assert.Equal(1, line.Frame.Height);
        Assert.Equal(note, line.Text);
        Assert.Equal(ShowMenuHandler.Padding, bar.Root.Frame.Y - line.Frame.Bottom);
        Assert.Equal(window.Viewport.Height - ShowMenuView.Footer, bar.Root.Frame.Y);
    }

    private static readonly MenuScreen SshMain = RemoteMenus.Screen(
        new ViewedFrom(ViewKind.Ssh, "homelab-box"), FleetAction.None);

    [Fact]
    public void The_window_of_a_screen_with_a_note_holds_that_note_as_a_line_of_its_own()
    {
        var composed = ShowMenuView.Compose(Keymap.Default, SshMain, _ => null);
        using var window = composed.Window;

        var note = Assert.Single(window.SubViews.OfType<Label>(), l => l.Text == ViewerHints.ScreensWhereYouSit);
        window.Layout();
        Assert.Equal(composed.List.Frame.Bottom + 1, note.Frame.Y);
        Assert.Contains(composed.List, window.SubViews);
        Assert.Contains(composed.Bar.Root, window.SubViews);
        Assert.Contains("fleet menu", window.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void The_window_of_a_screen_without_a_note_holds_no_extra_line()
    {
        var composed = ShowMenuView.Compose(Keymap.Default, RemoteMenus.Screen(null, FleetAction.None), _ => null);
        using var window = composed.Window;

        Assert.Empty(window.SubViews.OfType<Label>());
    }

    [Fact]
    public void The_rows_of_a_composed_screen_are_the_rows_of_that_screen_with_their_group_titles()
    {
        var composed = ShowMenuView.Compose(Keymap.Default, SshMain, a => a == FleetAction.OpenSettings ? "[x]" : null);
        using var window = composed.Window;

        Assert.Equal(FleetMenus.Actions(SshMain.Sections), composed.Items.Select(i => i.Action));
        Assert.Equal(["PROJECT on homelab-box", "MACHINE homelab-box"], composed.Items.Select(i => i.Header).OfType<string>());
        Assert.Equal("[x]", composed.Items.Single(i => i.Action == FleetAction.OpenSettings).Value);
    }

    [Fact]
    public void A_key_chooses_the_row_bound_to_it()
    {
        var items = new ShowMenuHandler(Keymap.Default).Items([FleetAction.Remotes, FleetAction.SaveSession, FleetAction.ThisMachineSettings]);

        Assert.Equal(1, ShowMenuView.RowFor(items, Keymap.Default, Keymap.Default.KeyFor(FleetAction.SaveSession)));
        Assert.Equal(-1, ShowMenuView.RowFor(items, Keymap.Default, Keymap.Default.KeyFor(FleetAction.QuitFleet)));
    }

    [Fact]
    public void An_empty_key_never_chooses_the_row_that_has_no_key()
    {
        var items = new ShowMenuHandler(Keymap.Default).Items([FleetAction.Remotes, FleetAction.ThisMachineSettings]);

        Assert.Equal(Terminal.Gui.Input.Key.Empty, Keymap.Default.KeyFor(FleetAction.ThisMachineSettings));
        Assert.Equal(-1, ShowMenuView.RowFor(items, Keymap.Default, Terminal.Gui.Input.Key.Empty));
    }

    [Fact]
    public void A_menu_is_wide_enough_for_its_note()
    {
        Assert.Equal(52, ShowMenuView.FitCols(10, 20, null));
        Assert.Equal(64 + 4, ShowMenuView.FitCols(10, 20, new string('x', 64)));
        Assert.Equal(90, ShowMenuView.FitCols(70, 20, new string('x', 64)));
    }

    [Fact]
    public void The_action_bar_measures_its_chips_and_the_gaps_between_them()
    {
        Assert.Equal(" enter select ".Length + 2, FleetActionBar.Measure([("enter", "select", () => { })], ButtonHints.Tooltips));
        Assert.Equal(
            " enter select ".Length + 2 + 1 + " ? keys ".Length + 2,
            FleetActionBar.Measure([("enter", "select", () => { }), ("?", "keys", () => { })], ButtonHints.Tooltips));
        Assert.Equal(0, FleetActionBar.Measure([], ButtonHints.Tooltips));
    }

    [Fact]
    public void No_items_produces_no_rows()
        => Assert.Empty(ShowMenuHandler.Rows([]));
}
