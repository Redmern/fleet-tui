using Fleet.Shared.Status;
using Fleet.Shared.Status.Models;

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
}
