using System.Security.Cryptography;
using System.Text;
using Fleet.Shared.Harness;
using Fleet.Shared.Status;
using Fleet.Shared.Status.Models;
using Fleet.Tests.Support;

namespace Fleet.Tests.Shared;

public sealed class PaneQuietTests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    public PaneQuietTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void A_pane_first_seen_counts_as_just_changed()
    {
        var mark = PaneQuiet.Next(null, "hello", T0);

        Assert.Equal(T0, mark.Since);
        Assert.False(PaneQuiet.Quiet(mark, T0.AddSeconds(30), TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void Unchanged_text_keeps_its_first_time_and_goes_quiet_after_the_window()
    {
        var first = PaneQuiet.Next(null, "hello", T0);
        var later = PaneQuiet.Next(first, "hello  \n\n", T0.AddMinutes(5));

        Assert.Equal(T0, later.Since);
        Assert.True(PaneQuiet.Quiet(later, T0.AddMinutes(5), TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void New_output_restarts_the_window()
    {
        var first = PaneQuiet.Next(null, "hello", T0);
        var changed = PaneQuiet.Next(first, "hello\nworking", T0.AddMinutes(5));

        Assert.Equal(T0.AddMinutes(5), changed.Since);
        Assert.False(PaneQuiet.Quiet(changed, T0.AddMinutes(5), TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void A_mark_survives_a_round_trip_through_the_agent_folder()
    {
        var first = PaneQuiet.Observe(_root, "hello", T0);
        var second = PaneQuiet.Observe(_root, "hello", T0.AddMinutes(3));

        Assert.Equal(first, second);
        Assert.True(File.Exists(Path.Combine(_root, ".fleet", PaneQuiet.FileName)));
    }

    [Fact]
    public void A_broken_mark_file_is_treated_as_unseen()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".fleet"));
        File.WriteAllText(Path.Combine(_root, ".fleet", PaneQuiet.FileName), "garbage");

        var mark = PaneQuiet.Observe(_root, "hello", T0);

        Assert.Equal(new PaneMark(PaneQuiet.Hash("hello"), T0), mark);
    }

    [Fact]
    public void An_unwritable_folder_still_gives_a_mark()
    {
        var mark = PaneQuiet.Observe(Path.Combine(_root, "missing", "\0bad"), "hello", T0);

        Assert.Equal(T0, mark.Since);
    }

    [Fact]
    public void A_changing_clock_or_spinner_does_not_restart_the_wait()
    {
        var first = PaneQuiet.Next(null, "output\n⠋ 12:01 (3s)", T0);
        var later = PaneQuiet.Next(first, "output\n⠹ 12:04 (184s)", T0.AddMinutes(3));

        Assert.Equal(T0, later.Since);
    }

    [Fact]
    public void A_profile_pattern_is_ignored_in_the_hash()
    {
        var patterns = HarnessRegistry.Opencode.VolatilePatterns;

        Assert.NotEqual(PaneQuiet.Hash("⬝■ busy"), PaneQuiet.Hash("■⬝ busy"));
        Assert.Equal(PaneQuiet.Hash("⬝■ busy", patterns), PaneQuiet.Hash("■⬝ busy", patterns));
    }

    [Fact]
    public void A_recorded_resize_that_only_reflows_does_not_restart_the_wait()
    {
        var first = PaneQuiet.Next(null, PaneCaptures.Read("claude", "idle-120-a"), T0);
        var resized = PaneQuiet.Next(first, PaneCaptures.Read("claude", "idle-90"), T0.AddMinutes(1));

        Assert.Equal(T0, resized.Since);
    }

    // A resize that is more than a reflow shows other text: claude and pi cut a path at another
    // place, pi scrolls lines off the top, opencode clips its status line or adds a sidebar, and a
    // lower pane shows fewer rows. fleet cannot tell that from output, so the wait restarts once,
    // and idle comes late, never early.
    [Theory]
    [InlineData("claude", "idle-120-a", "idle-150")]
    [InlineData("pi", "idle-120-a", "idle-90")]
    [InlineData("pi", "idle-120-a", "idle-150")]
    [InlineData("pi", "idle-120-a", "idle-120x30")]
    [InlineData("opencode", "idle-120-a", "idle-90")]
    [InlineData("opencode", "idle-120-a", "idle-150")]
    public void A_recorded_resize_that_shows_other_text_restarts_the_wait_once(string harness, string before, string after)
    {
        var patterns = HarnessRegistry.For(harness).VolatilePatterns;
        var first = PaneQuiet.Next(null, PaneCaptures.Read(harness, before), T0, patterns);
        var resized = PaneQuiet.Next(first, PaneCaptures.Read(harness, after), T0.AddMinutes(1), patterns);
        var still = PaneQuiet.Next(resized, PaneCaptures.Read(harness, after), T0.AddMinutes(2), patterns);

        Assert.Equal(T0.AddMinutes(1), resized.Since);
        Assert.Equal(T0.AddMinutes(1), still.Since);
    }

    [Fact]
    public void New_output_in_the_same_poll_as_a_resize_restarts_the_wait()
    {
        var first = PaneQuiet.Next(null, PaneCaptures.Read("claude", "idle-120-a"), T0);
        var changed = PaneQuiet.Next(first, PaneCaptures.Read("claude", "idle-90") + "\nnew output", T0.AddMinutes(1));

        Assert.Equal(T0.AddMinutes(1), changed.Since);
    }

    [Theory]
    [InlineData("claude", "idle-120-a", "changed-120")]
    [InlineData("pi", "fresh-120", "idle-120-a")]
    [InlineData("opencode", "fresh-120", "idle-120-a")]
    public void A_recorded_real_change_at_one_width_restarts_the_wait(string harness, string before, string after)
    {
        var patterns = HarnessRegistry.For(harness).VolatilePatterns;
        var first = PaneQuiet.Next(null, PaneCaptures.Read(harness, before), T0, patterns);
        var changed = PaneQuiet.Next(first, PaneCaptures.Read(harness, after), T0.AddMinutes(1), patterns);

        Assert.Equal(T0.AddMinutes(1), changed.Since);
    }

    [Fact]
    public void A_mark_file_with_more_fields_than_fleet_knows_still_reads()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".fleet"));
        File.WriteAllText(
            Path.Combine(_root, ".fleet", PaneQuiet.FileName), $"{PaneQuiet.Hash("hello")} 2026-10-28T12:00:00.0000000Z 120 more");

        Assert.Equal(T0, PaneQuiet.Observe(_root, "hello", T0.AddMinutes(4)).Since);
    }

    // A mark written before this normal form holds the hash of the line-trimmed text. It never
    // matches, so the wait restarts once and the agent goes idle one window later.
    [Fact]
    public void A_mark_with_an_old_style_hash_restarts_the_wait_once_and_then_goes_quiet()
    {
        var window = TimeSpan.FromSeconds(120);
        var oldStyle = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes("opencode >")));
        Directory.CreateDirectory(Path.Combine(_root, ".fleet"));
        File.WriteAllText(Path.Combine(_root, ".fleet", PaneQuiet.FileName), $"{oldStyle} 2026-10-28T11:00:00.0000000Z");

        var restarted = PaneQuiet.Observe(_root, "opencode > ", T0);
        var later = PaneQuiet.Observe(_root, "opencode > ", T0 + window);

        Assert.Equal(T0, restarted.Since);
        Assert.False(PaneQuiet.Quiet(restarted, T0, window));
        Assert.Equal(T0, later.Since);
        Assert.True(PaneQuiet.Quiet(later, T0 + window, window));
    }
}
