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
        FloatBorder.Reset();
    }

    public void Dispose()
    {
        FloatBorder.Reset();
        FleetKeyHints.Reset();
    }

    private void Enable() => Assert.True(FloatBorder.Enable(buttons =>
    {
        _published.Add(buttons);
        return true;
    }));

    [Fact]
    public void Corners_are_info_top_left_typing_the_reveal_toggle_and_close_top_right_typing_esc()
    {
        var shown = FloatBorder.For(true, [], false, null, keysShown: true, "?");
        var hidden = FloatBorder.For(true, [], false, null, keysShown: false, "?");

        Assert.Equal(
            [
                new FloatBorderButton(false, false, string.Empty, FleetIcons.Info, FloatBorder.RevealSend),
                new FloatBorderButton(false, true, "esc", FleetIcons.Close, "esc"),
            ],
            shown);
        Assert.Equal(
            [
                new FloatBorderButton(false, false, "?", FleetIcons.Info, FloatBorder.RevealSend),
                new FloatBorderButton(false, true, string.Empty, FleetIcons.Close, "esc"),
            ],
            hidden);
    }

    [Fact]
    public void Bar_chips_go_on_the_bottom_edge_and_keep_their_key_to_type_while_the_key_is_hidden()
    {
        var bar = new List<(string Key, string Label, Action Run)>
        {
            ("enter", "open", () => { }),
            ("bksp", "back", () => { }),
        };

        var buttons = FloatBorder.For(false, bar, alignRight: true, null, keysShown: false, "?");

        Assert.Equal(
            [
                new FloatBorderButton(true, true, string.Empty, "open", "enter"),
                new FloatBorderButton(true, true, string.Empty, "back", "backspace"),
            ],
            buttons);
    }

    [Theory]
    [InlineData("enter", "enter")]
    [InlineData("bksp", "backspace")]
    [InlineData("SHIFT", "shift+enter")]
    [InlineData("o/enter/A-Z", "o")]
    [InlineData("ctrl+r", "ctrl+r")]
    [InlineData("/", "/")]
    public void A_shown_key_maps_to_the_key_fleetd_types(string shown, string send)
    {
        Assert.Equal(send, FloatBorder.Send(shown));
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
                new FloatBorderButton(false, false, string.Empty, FleetIcons.Info, FloatBorder.RevealSend),
                new FloatBorderButton(false, true, "esc", FleetIcons.Close, "esc"),
                new FloatBorderButton(true, true, "enter", "open", "enter"),
            ],
            _published[^1]);

        FleetKeyHints.Apply(false);

        Assert.Equal("?", _published[^1][0].Key);
        Assert.Equal(string.Empty, _published[^1][2].Key);

        FloatBorder.Run(window, false);

        Assert.Empty(_published[^1]);
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
}
