using Fleet.Shared.Harness;
using Fleet.Shared.Status;
using Fleet.Tests.Support;

namespace Fleet.Tests.Shared;

public sealed class PaneSettledTests
{
    private static string Settled(string harness, string capture) =>
        PaneSettled.Text(PaneCaptures.Read(harness, capture), HarnessRegistry.For(harness).VolatilePatterns);

    [Theory]
    [InlineData("done at 14:08", "done at 14:09")]
    [InlineData("done 2:08 PM", "done 2:11 PM")]
    [InlineData("started 09:15:01", "started 09:15:59")]
    [InlineData("at 14:08:11Z", "at 14:08:59Z")]
    public void A_clock_is_masked(string before, string after) =>
        Assert.Equal(PaneSettled.Text(before), PaneSettled.Text(after));

    [Theory]
    [InlineData("Working (12s)", "Working (84s)")]
    [InlineData("retrying in 10h 50m", "retrying in 10h 49m")]
    [InlineData("took 1.2s", "took 3.4s")]
    [InlineData("Working (1m30s)", "Working (1m31s)")]
    [InlineData("up 3h12m", "up 3h13m")]
    public void An_elapsed_timer_is_masked(string before, string after) =>
        Assert.Equal(PaneSettled.Text(before), PaneSettled.Text(after));

    [Theory]
    [InlineData("3 tokens", "97 tokens")]
    [InlineData("↑ 1.2k tokens", "↑ 14.9k tokens")]
    [InlineData("↑12 ↓340", "↑15 ↓912")]
    [InlineData("$0.012 spent", "$1.40 spent")]
    public void A_token_or_cost_counter_is_masked(string before, string after) =>
        Assert.Equal(PaneSettled.Text(before), PaneSettled.Text(after));

    [Theory]
    [InlineData("⠋ Thinking", "⠹ Thinking")]
    [InlineData("| Thinking", "/ Thinking")]
    [InlineData("- Thinking", "\\ Thinking")]
    public void A_spinner_glyph_is_masked(string before, string after) =>
        Assert.Equal(PaneSettled.Text(before), PaneSettled.Text(after));

    [Fact]
    public void The_busy_line_is_dropped_whole_because_its_verb_changes_too() =>
        Assert.Equal(
            PaneSettled.Text("output line\n* Churning... (12s · esc to interrupt)\n  3 tokens"),
            PaneSettled.Text("output line\n* Brewing... (84s · esc to interrupt)\n  97 tokens"));

    // Masking the volatile part, not the whole line: a status bar that also names the model
    // must still show a model change.
    [Fact]
    public void The_rest_of_a_line_with_a_volatile_part_still_counts() =>
        Assert.NotEqual(
            PaneSettled.Text("$0.10 12:01 model-a"),
            PaneSettled.Text("$0.10 12:01 model-b"));

    // A clock is not part of a path or a file:line:column.
    [Theory]
    [InlineData("src/app.ts:12:34", "src/app.ts:12:56")]
    [InlineData("foo.cs:12:34: error", "foo.cs:13:10: error")]
    [InlineData("see notes/10:30", "see notes/10:45")]
    public void A_file_position_is_not_a_clock(string before, string after) =>
        Assert.NotEqual(PaneSettled.Text(before), PaneSettled.Text(after));

    // An elapsed timer is a whole number of minutes or hours, or seconds; "1.2m rows" is a count.
    [Theory]
    [InlineData("1.2m rows", "1.3m rows")]
    [InlineData("v1.2s", "v1.3s")]
    [InlineData("commit a1b2c3 12 s", "commit a1b2c3 13 s")]
    public void A_count_is_not_an_elapsed_timer(string before, string after) =>
        Assert.NotEqual(PaneSettled.Text(before), PaneSettled.Text(after));

    // Block and shade characters inside a line are output: a progress bar has no number to tell it by.
    [Theory]
    [InlineData("Downloading ████░░░░", "Downloading ██████░░")]
    [InlineData("████░░░░", "██████░░")]
    [InlineData("build ━━━━╸───── ok", "build ━━━━━━━╸── ok")]
    [InlineData("│ ███░ │", "│ ████ │")]
    public void A_bar_inside_a_line_is_kept(string before, string after) =>
        Assert.NotEqual(PaneSettled.Text(before), PaneSettled.Text(after));

    [Theory]
    [InlineData("exit code 1", "exit code 2")]
    [InlineData("45%", "46%")]
    [InlineData("12 files changed", "13 files changed")]
    [InlineData("hello", "hello\nnew output")]
    public void A_real_change_is_kept(string before, string after) =>
        Assert.NotEqual(PaneSettled.Text(before), PaneSettled.Text(after));

    [Fact]
    public void A_soft_wrap_at_another_width_settles_the_same() =>
        Assert.Equal(
            PaneSettled.Text("a long sentence that the harness wraps\nat the pane width, even mid-wo\nrd"),
            PaneSettled.Text("a long sentence that the harness\nwraps at the pane width, even mid-word"));

    [Fact]
    public void Borders_and_padding_that_follow_the_width_are_dropped() =>
        Assert.Equal(
            PaneSettled.Text("╭────────╮\n│ hi     │\n╰────────╯"),
            PaneSettled.Text("╭────╮\r\n│ hi │\r\n╰────╯"));

    [Fact]
    public void A_profile_pattern_masks_what_it_matches()
    {
        var patterns = HarnessRegistry.Opencode.VolatilePatterns;

        Assert.NotEqual(PaneSettled.Text("⬝⬝■■ waiting"), PaneSettled.Text("■■⬝⬝ waiting"));
        Assert.Equal(PaneSettled.Text("⬝⬝■■ waiting", patterns), PaneSettled.Text("■■⬝⬝ waiting", patterns));
    }

    // Only opencode showed a volatile part on an idle screen (a countdown and a progress bar);
    // the idle screens of claude and pi did not change at all, so they have no pair here.
    [Theory]
    [InlineData("idle-120-a", "idle-120-b")]
    [InlineData("idle-120-b", "idle-120-c")]
    public void A_recorded_idle_pair_settles_the_same(string first, string second)
    {
        Assert.NotEqual(PaneCaptures.Read("opencode", first), PaneCaptures.Read("opencode", second));
        Assert.Equal(Settled("opencode", first), Settled("opencode", second));
    }

    [Theory]
    [InlineData("claude", "idle-120-a", "idle-90")]
    public void A_recorded_resize_that_only_reflows_settles_the_same(string harness, string first, string second) =>
        Assert.Equal(Settled(harness, first), Settled(harness, second));

    [Theory]
    [InlineData("claude", "idle-120-a", "changed-120")]
    [InlineData("pi", "fresh-120", "idle-120-a")]
    [InlineData("opencode", "fresh-120", "idle-120-a")]
    public void A_recorded_real_change_settles_differently(string harness, string first, string second) =>
        Assert.NotEqual(Settled(harness, first), Settled(harness, second));
}
