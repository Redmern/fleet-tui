using Fleet.Ui;

namespace Fleet.Tests.Ui;

public sealed class FleetKeyHintsTests : IDisposable
{
    public FleetKeyHintsTests() => FleetKeyHints.Reset();

    public void Dispose() => FleetKeyHints.Reset();

    [Fact]
    public void Keys_show_by_default_and_hide_when_the_setting_is_off()
    {
        Assert.True(FleetKeyHints.Shown);

        FleetKeyHints.Apply(false);

        Assert.False(FleetKeyHints.Shown);
    }

    [Fact]
    public void Question_mark_toggles_the_keys_until_pressed_again()
    {
        FleetKeyHints.Apply(false);

        FleetKeyHints.Toggle();
        Assert.True(FleetKeyHints.Shown);

        FleetKeyHints.Toggle();
        Assert.False(FleetKeyHints.Shown);
    }

    [Fact]
    public void Turning_the_setting_on_and_off_again_forgets_the_toggle()
    {
        FleetKeyHints.Apply(false);
        FleetKeyHints.Toggle();

        FleetKeyHints.Apply(true);
        FleetKeyHints.Apply(false);

        Assert.False(FleetKeyHints.Shown);
    }

    [Fact]
    public void Holding_shows_the_keys_until_the_key_is_released()
    {
        FleetKeyHints.Apply(false);

        FleetKeyHints.Hold(1000);
        Assert.True(FleetKeyHints.Shown);

        FleetKeyHints.Released();
        Assert.False(FleetKeyHints.Shown);
    }

    // Terminal.Gui's ansi driver (and most terminals) send no key-up, so a hold
    // follows the key's auto-repeat: it waits out the first repeat delay, then
    // ends a few repeat intervals after the repeats stop.
    [Fact]
    public void A_tap_without_repeats_shows_the_keys_through_the_first_repeat_delay()
    {
        FleetKeyHints.Apply(false);
        FleetKeyHints.Hold(1000);

        Assert.False(FleetKeyHints.Lapsed(1000 + 600));
        Assert.True(FleetKeyHints.Lapsed(1000 + (long)FleetKeyHints.FirstRepeat.TotalMilliseconds));
    }

    [Fact]
    public void Once_the_key_repeats_the_hold_ends_soon_after_the_repeats_stop()
    {
        FleetKeyHints.Apply(false);
        FleetKeyHints.Hold(1000);
        FleetKeyHints.Hold(1500);
        FleetKeyHints.Hold(1530);
        FleetKeyHints.Hold(1560);

        Assert.Equal(FleetKeyHints.ShortestLapse, FleetKeyHints.Window);
        Assert.False(FleetKeyHints.Lapsed(1560 + 100));
        Assert.True(FleetKeyHints.Lapsed(1560 + 150));
    }

    [Fact]
    public void A_slow_repeat_rate_widens_the_window()
    {
        FleetKeyHints.Apply(false);
        FleetKeyHints.Hold(1000);
        FleetKeyHints.Hold(1100);

        Assert.Equal(TimeSpan.FromMilliseconds(100 * FleetKeyHints.RepeatsMissed), FleetKeyHints.Window);
    }

    [Fact]
    public void A_new_press_after_a_release_waits_for_the_first_repeat_again()
    {
        FleetKeyHints.Apply(false);
        FleetKeyHints.Hold(1000);
        FleetKeyHints.Hold(1030);
        FleetKeyHints.Release();

        FleetKeyHints.Hold(5000);

        Assert.Equal(FleetKeyHints.FirstRepeat, FleetKeyHints.Window);
    }

    [Fact]
    public void Once_a_release_was_seen_a_hold_waits_for_it()
    {
        FleetKeyHints.Apply(false);
        FleetKeyHints.Hold(1000);
        FleetKeyHints.Released();

        FleetKeyHints.Hold(2000);

        Assert.False(FleetKeyHints.Lapsed(2000 + 10_000));
        Assert.True(FleetKeyHints.Shown);
    }

    [Fact]
    public void Changed_fires_only_when_visibility_changes()
    {
        var changes = 0;
        FleetKeyHints.Changed += () => changes++;

        FleetKeyHints.Apply(true);
        FleetKeyHints.Toggle();
        Assert.Equal(0, changes);

        FleetKeyHints.Apply(false);
        Assert.Equal(1, changes);

        FleetKeyHints.Hold(1000);
        FleetKeyHints.Hold(1050);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Hidden_keys_leave_only_the_labels_on_the_buttons_except_a_pinned_one()
    {
        IReadOnlyList<(string, string, Action)> items =
        [
            ("enter", "select", () => { }),
            ("?", "keys", () => { }),
            ("q/esc", "close", () => { }),
        ];

        Assert.Equal(items, FleetActionBar.Visible(items, keys: true, pinned: "?"));
        Assert.Equal(
            ["", "?", ""],
            FleetActionBar.Visible(items, keys: false, pinned: "?").Select(i => i.Key));
        Assert.Equal(
            ["select", "keys", "close"],
            FleetActionBar.Visible(items, keys: false, pinned: "?").Select(i => i.Label));
    }
}
