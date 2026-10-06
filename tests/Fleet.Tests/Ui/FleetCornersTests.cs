using Fleet.Ui;
using Fleet.Ui.Constants;

namespace Fleet.Tests.Ui;

public sealed class FleetCornersTests : IDisposable
{
    public FleetCornersTests() => FleetKeyHints.Reset();

    public void Dispose() => FleetKeyHints.Reset();

    [Fact]
    public void The_help_corner_is_a_button_with_the_reveal_key_and_the_info_icon_while_keys_are_hidden()
    {
        Assert.Equal(
            $"{FleetGlyphs.PillLeft} ? {FleetIcons.Info} {FleetGlyphs.PillRight}",
            string.Concat(FleetCorners.Help(keysShown: false, "?").Select(s => s.Text)));
    }

    [Fact]
    public void The_help_corner_drops_the_reveal_key_but_keeps_its_width_while_keys_are_shown()
    {
        var hidden = string.Concat(FleetCorners.Help(keysShown: false, "ctrl+k").Select(s => s.Text));
        var shown = string.Concat(FleetCorners.Help(keysShown: true, "ctrl+k").Select(s => s.Text));

        Assert.Equal($"{FleetGlyphs.PillLeft} {FleetIcons.Info} {FleetGlyphs.PillRight}       ", shown);
        Assert.Equal(hidden.Length, shown.Length);
    }

    [Fact]
    public void The_close_corner_is_a_button_with_the_close_icon_and_esc_while_keys_are_shown()
    {
        var hidden = string.Concat(FleetCorners.Close(keysShown: false).Select(s => s.Text));
        var shown = string.Concat(FleetCorners.Close(keysShown: true).Select(s => s.Text));

        Assert.Equal($"    {FleetGlyphs.PillLeft} {FleetIcons.Close} {FleetGlyphs.PillRight}", hidden);
        Assert.Equal($"{FleetGlyphs.PillLeft} esc {FleetIcons.Close} {FleetGlyphs.PillRight}", shown);
        Assert.Equal(hidden.Length, shown.Length);
        Assert.Equal(FleetIcons.Close, FleetIcons.For(Fleet.Shared.Keymap.Enums.FleetAction.Close));
    }

    [Fact]
    public void A_tab_bar_goes_under_the_corner_buttons_across_the_full_width()
    {
        using var window = new Terminal.Gui.ViewBase.View { Width = 60, Height = 10 };
        var tabs = new Terminal.Gui.ViewBase.View { X = 1, Y = 0, Height = 2 };
        window.Add(tabs);

        FleetCorners.Attach(window, () => { }, below: tabs);
        window.Layout();

        Assert.Equal(1, tabs.Frame.Y);
        Assert.Equal(1, tabs.Frame.X);
        Assert.Equal(58, tabs.Frame.Width);
    }

    [Fact]
    public void Every_window_with_corners_gets_a_blank_row_over_the_corners_and_under_the_bottom_bar()
    {
        using var window = new Terminal.Gui.Views.Window { Width = 60, Height = 12, BorderStyle = Terminal.Gui.Drawing.LineStyle.None };
        var bar = new FleetActionBar(Terminal.Gui.ViewBase.Pos.AnchorEnd(1));
        window.Add(bar.Root);

        FleetCorners.Attach(window, () => { });
        window.Layout();

        Assert.Equal(1, window.Viewport.Y + window.Padding!.Thickness.Top);
        Assert.Equal(12 - FleetCorners.Rows, window.Viewport.Height);
        Assert.Equal(window.Viewport.Height - 1, bar.Root.Frame.Y);
    }

    [Fact]
    public void The_reveal_key_defaults_to_a_question_mark()
    {
        Assert.Equal("?", FleetKeyHints.RevealKey);
    }

    [Fact]
    public void A_rebound_reveal_key_tells_the_corners_to_refit()
    {
        var changes = 0;
        FleetKeyHints.Changed += () => changes++;

        FleetKeyHints.Rebind("?");
        FleetKeyHints.Rebind("ctrl+k");

        Assert.Equal("ctrl+k", FleetKeyHints.RevealKey);
        Assert.Equal(1, changes);
    }
}
