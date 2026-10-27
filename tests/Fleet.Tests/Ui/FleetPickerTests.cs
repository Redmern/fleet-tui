using System.Reflection;
using Fleet.Ui;

namespace Fleet.Tests.Ui;

public class FleetPickerTests
{
    private static string Source { get; } = File.ReadAllText(Path.Combine(
        typeof(FleetPickerTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot")
            .Value!,
        "src", "Fleet", "Ui", "FleetPicker.cs"));

    [Fact]
    public void Filtering_and_refreshing_are_off_unless_a_picker_asks_for_them()
    {
        var options = new FleetPickerOptions();

        Assert.False(options.Filter);
        Assert.Null(options.Refresh);
    }

    [Fact]
    public void The_picker_is_a_centered_card_like_the_dialogs()
    {
        Assert.Contains("FleetTheme.Modal(", Source);
        Assert.DoesNotContain("FleetTheme.Overlay(", Source);
    }

    // A filter needs every bare key for typing, so letter accelerators and
    // the j/k motions are left off while filtering.
    [Fact]
    public void A_filtering_picker_types_letters_instead_of_using_accelerators()
    {
        Assert.Contains("PickerFilter.Edit(", Source);
        Assert.Contains("filtering ? [.. entries.Select(_ => string.Empty)]", Source);
    }

    [Fact]
    public void Backspace_only_goes_back_once_the_filter_is_empty()
    {
        Assert.Contains("FleetKeys.GoesBack(key, text)", Source);
    }

    [Fact]
    public void An_empty_filter_result_cannot_be_taken()
    {
        Assert.Contains("visible.Count == 0", Source);
        Assert.Contains("FleetSpan.Muted(NoMatches)", Source);
        Assert.Equal("no matches", FleetPicker.NoMatches);
    }

    // AC-13/AC-16: the refresh runs off the UI thread, lands through
    // app.Invoke, and is dropped once the picker has closed.
    [Fact]
    public void A_refresh_runs_in_the_background_and_is_dropped_after_close()
    {
        Assert.Contains("Task.Run(", Source);
        Assert.Contains("app.Invoke(", Source);
        Assert.Contains("if (!open)", Source);
        Assert.Contains("closing.Cancel()", Source);
        Assert.Contains("PickerFilter.Merge(", Source);
        Assert.Contains("FleetGlyphs.Frame(", Source);
    }

    // A refresh that throws must not leave the spinner running: it ends as a note.
    [Fact]
    public void A_refresh_that_throws_stops_the_spinner_with_an_offline_note()
    {
        Assert.Contains("catch (Exception e)", Source);
        Assert.Contains("new PickerRefresh([], $\"offline: {e.Message}\")", Source);
        Assert.Contains("PickerFilter.OneLine(found.Note", Source);
    }
}
