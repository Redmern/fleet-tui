using Fleet.Features.Updates.ListReleases;
using Fleet.Ports.Releases.Models;

namespace Fleet.Tests.Features.Updates;

public class ReleaseListFormatterTests
{
    private static ReleaseInfo Stable(string tag) => new(tag, []);

    private static ReleaseInfo Pre(string tag) => new(tag, [], Prerelease: true);

    private static IReadOnlyList<ReleaseInfo> StableRun(int count) =>
        [.. Enumerable.Range(0, count).Select(i => Stable($"v0.7.{count - i}"))];

    [Fact]
    public void The_header_names_the_installed_and_latest_stable_versions()
    {
        var lines = ReleaseListFormatter.Lines([Pre("v0.8.0-rc.1"), Stable("v0.7.20"), Stable("v0.7.19")], "0.7.19", all: false);

        Assert.Equal("Installed  v0.7.19", lines[0]);
        Assert.Equal("Latest     v0.7.20", lines[1]);
    }

    [Fact]
    public void The_header_says_unknown_when_there_is_no_stable_release()
    {
        var lines = ReleaseListFormatter.Lines([Pre("v0.8.0-rc.1")], "0.7.19", all: false);

        Assert.Equal("Latest     unknown", lines[1]);
    }

    [Fact]
    public void By_default_only_the_newest_ten_stable_releases_are_shown()
    {
        var releases = StableRun(15).Prepend(Pre("v0.8.0-rc.1")).ToList();

        var rows = Rows(ReleaseListFormatter.Lines(releases, "0.7.15", all: false));

        Assert.Equal(10, rows.Count);
        Assert.StartsWith("  v0.7.15", rows[0], StringComparison.Ordinal);
        Assert.StartsWith("  v0.7.6", rows[9], StringComparison.Ordinal);
        Assert.DoesNotContain(rows, r => r.Contains("rc.1", StringComparison.Ordinal));
    }

    [Fact]
    public void Rows_are_an_aligned_table_marked_installed_and_latest()
    {
        var lines = ReleaseListFormatter.Lines([Stable("v0.7.10"), Stable("v0.7.9"), Stable("v0.7.8")], "0.7.9", all: false);

        Assert.Equal(
            ["  v0.7.10  latest", "  v0.7.9   installed", "  v0.7.8"],
            Rows(lines));
    }

    [Fact]
    public void One_release_can_be_both_installed_and_latest()
    {
        var rows = Rows(ReleaseListFormatter.Lines([Stable("v0.7.10")], "0.7.10", all: false));

        Assert.Equal("  v0.7.10  installed, latest", rows[0]);
    }

    [Fact]
    public void A_footer_counts_the_releases_left_out()
    {
        var releases = StableRun(13).Prepend(Pre("v0.8.0-rc.1")).ToList();

        var lines = ReleaseListFormatter.Lines(releases, "0.7.13", all: false);

        Assert.Equal("  ... 4 older, use --all", lines[^1]);
    }

    [Fact]
    public void No_footer_when_everything_is_shown()
    {
        var lines = ReleaseListFormatter.Lines(StableRun(10), "0.7.10", all: false);

        Assert.DoesNotContain(lines, l => l.Contains("--all", StringComparison.Ordinal));
    }

    [Fact]
    public void All_shows_every_release_with_prerelease_markers_and_no_footer()
    {
        var releases = StableRun(12).Prepend(Pre("v0.8.0-rc.1")).ToList();

        var lines = ReleaseListFormatter.Lines(releases, "0.7.12", all: true);
        var rows = Rows(lines);

        Assert.Equal(13, rows.Count);
        Assert.Equal("  v0.8.0-rc.1  prerelease", rows[0]);
        Assert.Equal("  v0.7.12      installed, latest", rows[1]);
        Assert.DoesNotContain(lines, l => l.Contains("--all", StringComparison.Ordinal));
    }

    [Fact]
    public void An_installed_version_outside_the_shown_rows_is_still_in_the_header()
    {
        var lines = ReleaseListFormatter.Lines(StableRun(15), "0.7.1", all: false);

        Assert.Equal("Installed  v0.7.1", lines[0]);
        Assert.DoesNotContain(lines, l => l.Contains("installed", StringComparison.Ordinal));
    }

    [Fact]
    public void No_releases_means_no_lines() =>
        Assert.Empty(ReleaseListFormatter.Lines([], "0.7.1", all: false));

    private static List<string> Rows(IReadOnlyList<string> lines) =>
        [.. lines.Skip(3).Where(l => !l.Contains("--all", StringComparison.Ordinal))];
}
