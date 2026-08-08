using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Keymap.Models;
using Fleet.Ui;
using Fleet.Ui.Enums;
using Terminal.Gui.Input;

namespace Fleet.Tests.Ui;

public class PrefixRecognizerTests
{
    private static Key Prefix => Keymap.Default.Prefix;

    private static PrefixRecognizer New() => new(Keymap.Default);

    [Fact]
    public void An_ordinary_key_is_not_for_fleet()
    {
        var result = New().Feed(Key.Z);

        Assert.Equal(PrefixOutcome.NotForFleet, result.Outcome);
        Assert.False(result.Handled);
    }

    [Fact]
    public void The_prefix_arms_the_recognizer_and_is_consumed()
    {
        var recognizer = New();

        var result = recognizer.Feed(Prefix);

        Assert.Equal(PrefixOutcome.Armed, result.Outcome);
        Assert.True(result.Handled);
        Assert.True(recognizer.Armed);
    }

    [Fact]
    public void Ctrl_s_is_not_the_prefix_because_wezterm_claims_it_as_a_leader()
    {
        var recognizer = New();

        Assert.Equal(PrefixOutcome.NotForFleet, recognizer.Feed(Key.S.WithCtrl).Outcome);
        Assert.False(recognizer.Armed);
    }

    [Fact]
    public void Prefix_then_space_opens_the_menu()
    {
        var recognizer = New();
        recognizer.Feed(Prefix);

        var result = recognizer.Feed(Key.Space);

        Assert.Equal(PrefixOutcome.Action, result.Outcome);
        Assert.Equal(FleetAction.OpenMenu, result.Action);
        Assert.False(recognizer.Armed);
    }

    [Fact]
    public void Prefix_then_an_unbound_key_cancels_without_acting()
    {
        var recognizer = New();
        recognizer.Feed(Prefix);

        var result = recognizer.Feed(Key.F12);

        Assert.Equal(PrefixOutcome.Cancelled, result.Outcome);
        Assert.Equal(FleetAction.None, result.Action);
        Assert.False(recognizer.Armed);
    }

    [Fact]
    public void Prefix_then_escape_cancels()
    {
        var recognizer = New();
        recognizer.Feed(Prefix);

        Assert.Equal(PrefixOutcome.Cancelled, recognizer.Feed(Key.Esc).Outcome);
    }

    [Fact]
    public void Prefix_twice_stays_armed()
    {
        var recognizer = New();
        recognizer.Feed(Prefix);

        var result = recognizer.Feed(Prefix);

        Assert.Equal(PrefixOutcome.Armed, result.Outcome);
        Assert.True(recognizer.Armed);
    }

    [Fact]
    public void Without_the_prefix_a_bound_action_key_is_not_claimed()
    {
        var result = New().Feed(Key.Space);

        Assert.Equal(PrefixOutcome.NotForFleet, result.Outcome);
    }

    [Fact]
    public void A_custom_prefix_is_honoured_and_the_default_stops_working()
    {
        var recognizer = new PrefixRecognizer(
            new Keymap(KeymapConfig.Default.WithPrefix("Ctrl+B")));

        Assert.Equal(PrefixOutcome.NotForFleet, recognizer.Feed(Prefix).Outcome);
        Assert.Equal(PrefixOutcome.Armed, recognizer.Feed(Key.B.WithCtrl).Outcome);
    }

    [Fact]
    public void Prefix_then_a_rebound_menu_key_still_opens_the_menu()
    {
        var keymap = new Keymap(KeymapConfig.Default.With(FleetAction.OpenMenu, "m"));
        var recognizer = new PrefixRecognizer(keymap);

        recognizer.Feed(keymap.Prefix);

        Assert.Equal(FleetAction.OpenMenu, recognizer.Feed(Key.M).Action);
    }
}
