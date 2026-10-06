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
