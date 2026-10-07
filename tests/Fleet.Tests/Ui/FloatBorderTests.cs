using Fleet.Ui;
using Fleet.Ui.Constants;
using Fleet.Ui.Models;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Fleet.Tests.Ui;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class FloatBorderCollection
{
    public const string Name = "float-border";
}

[Collection(FloatBorderCollection.Name)]
public sealed class FloatBorderTests : IDisposable
{
    private readonly List<IReadOnlyList<FloatBorderButton>> _published = [];

    public FloatBorderTests()
    {
        FleetKeyHints.Reset();
        FleetButtonHints.Reset();
        FloatBorder.Reset();
    }

    public void Dispose()
    {
        FloatBorder.Reset();
        FleetButtonHints.Reset();
        FleetKeyHints.Reset();
    }

    private void Enable() => Assert.True(FloatBorder.Enable(buttons =>
    {
        _published.Add(buttons);
        return true;
    }));

    [Fact]
    public void Corners_are_info_top_left_and_close_top_right_typing_the_first_two_reserved_keys()
    {
        var shown = FloatBorder.For(true, [], false, null, keysShown: true, "?");
        var hidden = FloatBorder.For(true, [], false, null, keysShown: false, "?");

        Assert.Equal(
            [
                new FloatBorderButton(false, false, string.Empty, FleetIcons.Info, "f1"),
                new FloatBorderButton(false, true, "esc", FleetIcons.Close, "f2"),
            ],
            shown);
        Assert.Equal(
            [
                new FloatBorderButton(false, false, "?", FleetIcons.Info, "f1"),
                new FloatBorderButton(false, true, string.Empty, FleetIcons.Close, "f2"),
            ],
            hidden);
    }

    [Fact]
    public void Bar_chips_go_on_the_bottom_edge_each_with_its_own_reserved_key_while_the_key_is_hidden()
    {
        var bar = new List<(string Key, string Label, Action Run)>
        {
            ("enter", "open", () => { }),
            ("bksp", "back", () => { }),
        };

        var buttons = FloatBorder.For(false, bar, alignRight: true, null, keysShown: false, "?");

        Assert.Equal(
            [
                new FloatBorderButton(true, true, string.Empty, "open", "f1"),
                new FloatBorderButton(true, true, string.Empty, "back", "f2"),
            ],
            buttons);
    }

    [Fact]
    public void In_text_mode_the_border_buttons_carry_the_icon_name_like_the_in_content_ones()
    {
        var bar = new List<(string Key, string Label, Action Run)> { ("enter", FleetIcons.Select, () => { }) };

        var text = FloatBorder.For(true, bar, true, null, keysShown: true, "?", Fleet.Shared.Settings.Enums.ButtonHints.Text);
        var none = FloatBorder.For(true, bar, true, null, keysShown: true, "?", Fleet.Shared.Settings.Enums.ButtonHints.None);

        Assert.Equal(
            [
                FleetButtonHints.Face(FleetIcons.Info, Fleet.Shared.Settings.Enums.ButtonHints.Text),
                FleetButtonHints.Face(FleetIcons.Close, Fleet.Shared.Settings.Enums.ButtonHints.Text),
                FleetButtonHints.Face(FleetIcons.Select, Fleet.Shared.Settings.Enums.ButtonHints.Text),
            ],
            text.Select(b => b.Label));
        Assert.Contains(FleetIcons.Name(FleetIcons.Select), text[2].Label);
        Assert.Equal([FleetIcons.Info, FleetIcons.Close, FleetIcons.Select], none.Select(b => b.Label));
    }

    [Fact]
    public void Changing_the_button_hints_setting_republishes_the_border_buttons()
    {
        Enable();
        using var window = new Window();
        FleetCorners.Attach(window, () => { });
        FloatBorder.Run(window, true);

        FleetButtonHints.Apply(Fleet.Shared.Settings.Enums.ButtonHints.Text);

        Assert.Equal(FleetButtonHints.Face(FleetIcons.Close, Fleet.Shared.Settings.Enums.ButtonHints.Text), _published[^1][1].Label);
    }

    [Fact]
    public void Chips_past_the_twelfth_reserved_key_get_no_key_to_type()
    {
        var bar = Enumerable.Range(0, 12).Select(i => ($"{i}", $"chip {i}", (Action)(() => { }))).ToList();

        var buttons = FloatBorder.For(true, bar, false, null, keysShown: true, "?");

        Assert.Equal("f12", buttons[11].Send);
        Assert.Equal(string.Empty, buttons[12].Send);
    }

    [Fact]
    public void It_stays_off_when_fleetd_refuses_the_first_empty_list()
    {
        Assert.False(FloatBorder.Enable(_ => false));
        Assert.False(FloatBorder.Enabled);
        Assert.Equal(2 * FleetCorners.Margin, FleetCorners.Rows);
    }

    [Fact]
    public void In_a_bordered_float_the_window_gets_no_corner_views_no_padding_and_a_hidden_bar()
    {
        Enable();
        using var window = new Window { Width = 60, Height = 12, BorderStyle = Terminal.Gui.Drawing.LineStyle.None };
        var tabs = new View { X = 1, Y = 0, Height = 2 };
        var bar = new FleetActionBar(Pos.AnchorEnd(1));
        window.Add(tabs, bar.Root);

        FleetCorners.Attach(window, () => { }, below: tabs);
        window.Layout();

        Assert.Equal(0, FleetCorners.Rows);
        Assert.Equal(0, window.Padding!.Thickness.Top);
        Assert.Equal(0, tabs.Frame.Y);
        Assert.Equal(2, window.SubViews.Count);
        Assert.False(bar.Root.Visible);
    }

    [Fact]
    public void The_running_windows_corners_and_bar_are_published_and_republished_when_keys_toggle()
    {
        Enable();
        using var window = new Window();
        var bar = new FleetActionBar(Pos.AnchorEnd(1), alignRight: true);
        window.Add(bar.Root);
        bar.Show([("enter", "open", () => { })]);
        FleetCorners.Attach(window, () => { });

        FloatBorder.Run(window, true);

        Assert.Equal(
            [
                new FloatBorderButton(false, false, string.Empty, FleetIcons.Info, "f1"),
                new FloatBorderButton(false, true, "esc", FleetIcons.Close, "f2"),
                new FloatBorderButton(true, true, "enter", "open", "f3"),
            ],
            _published[^1]);

        FleetKeyHints.Apply(false);

        Assert.Equal("?", _published[^1][0].Key);
        Assert.Equal(string.Empty, _published[^1][2].Key);

        FloatBorder.Run(window, false);

        Assert.Empty(_published[^1]);
    }

    [Fact]
    public void A_reserved_key_runs_the_action_its_in_content_button_ran()
    {
        Enable();
        using var window = new Window();
        var closed = 0;
        var backs = 0;
        var bar = new FleetActionBar(Pos.AnchorEnd(1));
        window.Add(bar.Root);
        bar.Show([("enter", "open", () => { }), ("bksp", "back", () => backs++)]);
        FleetCorners.Attach(window, () => closed++);
        FloatBorder.Run(window, true);
        FleetKeyHints.Apply(false);

        Assert.True(FloatBorder.Press(Terminal.Gui.Input.Key.F2));
        Assert.True(FloatBorder.Press(Terminal.Gui.Input.Key.F4));
        Assert.True(FloatBorder.Press(Terminal.Gui.Input.Key.F1));
        Assert.False(FloatBorder.Press(Terminal.Gui.Input.Key.F5));
        Assert.False(FloatBorder.Press(Terminal.Gui.Input.Key.Esc));

        Assert.Equal((1, 1), (closed, backs));
        Assert.True(FleetKeyHints.Shown);
    }

    [Fact]
    public void A_publish_fleetd_refuses_is_sent_again_on_the_next_refresh()
    {
        var accept = true;
        Assert.True(FloatBorder.Enable(buttons =>
        {
            _published.Add(buttons);
            return accept;
        }));
        using var window = new Window();
        FleetCorners.Attach(window, () => { });

        accept = false;
        FloatBorder.Run(window, true);
        accept = true;
        FloatBorder.Refresh();

        Assert.Equal(2, _published[^1].Count);
        Assert.Equal(_published[^2], _published[^1]);
    }

    [Fact]
    public void A_dialog_over_a_window_publishes_its_own_buttons_and_the_window_gets_its_back_when_it_closes()
    {
        Enable();
        using var screen = new Window();
        using var dialog = new Window();
        var bar = new FleetActionBar(Pos.AnchorEnd(1));
        screen.Add(bar.Root);
        bar.Show([("enter", "open", () => { })]);
        FleetCorners.Attach(screen, () => { });
        FleetCorners.Attach(dialog, () => { });

        FloatBorder.Run(screen, true);
        FloatBorder.Run(dialog, true);

        Assert.Equal(2, _published[^1].Count);

        FloatBorder.Run(dialog, false);

        Assert.Equal(3, _published[^1].Count);
    }

    [Fact]
    public void Framed_mode_borders_only_the_window_attached_as_framed_and_leaves_dialogs_their_own_bars()
    {
        Assert.True(FloatBorder.Enable(
            buttons =>
            {
                _published.Add(buttons);
                return true;
            },
            framed: true));
        using var dashboard = new Window();
        using var dialog = new Window();
        var bar = new FleetActionBar(Pos.AnchorEnd(1));
        var dialogBar = new FleetActionBar(Pos.AnchorEnd(1));
        dashboard.Add(bar.Root);
        dialog.Add(dialogBar.Root);
        bar.Show([("n", "new", () => { })]);
        dialogBar.Show([("enter", "open", () => { })]);
        FleetCorners.Attach(dashboard, null, framed: true);
        FleetCorners.Attach(dialog, () => { });

        FloatBorder.Run(dashboard, true);

        Assert.False(FloatBorder.Enabled);
        Assert.False(bar.Root.Visible);
        Assert.True(dialogBar.Root.Visible);
        Assert.Equal(
            [
                new FloatBorderButton(false, false, string.Empty, FleetIcons.Info, "f1"),
                new FloatBorderButton(true, false, "n", "new", "f2"),
            ],
            _published[^1]);

        FloatBorder.Run(dialog, true);

        Assert.Empty(_published[^1]);

        FloatBorder.Run(dialog, false);

        Assert.Equal(2, _published[^1].Count);
    }

    [Fact]
    public void A_framed_window_without_close_maps_the_reserved_keys_to_help_then_its_bar()
    {
        Assert.True(FloatBorder.Enable(_ => true, framed: true));
        using var window = new Window();
        var news = 0;
        var bar = new FleetActionBar(Pos.AnchorEnd(1));
        window.Add(bar.Root);
        bar.Show([("n", "new", () => news++)]);
        FleetCorners.Attach(window, null, framed: true);
        FloatBorder.Run(window, true);
        FleetKeyHints.Apply(false);

        Assert.True(FloatBorder.Press(Terminal.Gui.Input.Key.F2));
        Assert.True(FloatBorder.Press(Terminal.Gui.Input.Key.F1));
        Assert.False(FloatBorder.Press(Terminal.Gui.Input.Key.F3));

        Assert.Equal(1, news);
        Assert.True(FleetKeyHints.Shown);
    }
}
