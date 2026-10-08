using System.Reflection;
using System.Text.RegularExpressions;
using Fleet.Features.Menu.ShowReleaseNotes;
using Fleet.Features.Menu.ShowReleaseNotes.Models;

namespace Fleet.Tests.Features.Menu;

public partial class ReleaseNotesTests
{
    private const string Sample = """
        # Release notes

        Intro text that is not an entry.

        ## 0.6.0.2 (2026-10-02)

        - Remotes have nicknames.
        - A bullet that goes on
          over two lines.

        ## 0.6.0 (2026-10-01)

        - Built-in multiplexer.

        ## 0.5.24

        - `fleet update 1.2.3` works.

        # Earlier builds (before the restart)

        Old numbers.

        ## 0.6.0 (2026-09-01)

        - An old build with the same number.
        """;

    private static string RepoRoot { get; } =
        typeof(ReleaseNotesTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot").Value!;

    [Fact]
    public void Parse_reads_each_heading_with_its_date_and_bullets()
    {
        var entries = ReleaseNotes.Parse(Sample);

        Assert.Equal(["0.6.0.2", "0.6.0", "0.5.24", "0.6.0"], entries.Select(e => e.Version));
        Assert.Equal([false, false, false, true], entries.Select(e => e.Legacy));
        Assert.Equal("2026-10-02", entries[0].Date);
        Assert.Null(entries[2].Date);
        Assert.Equal(["Remotes have nicknames.", "A bullet that goes on over two lines."], entries[0].Bullets);
        Assert.Equal(["`fleet update 1.2.3` works."], entries[2].Bullets);
    }

    [Fact]
    public void Parse_handles_crlf_line_endings()
    {
        var entries = ReleaseNotes.Parse(Sample.Replace("\n", "\r\n", StringComparison.Ordinal));

        Assert.Equal("Remotes have nicknames.", entries[0].Bullets[0]);
        Assert.Equal("2026-10-02", entries[0].Date);
    }

    [Theory]
    [InlineData("0.6.0", "0.6")]
    [InlineData("0.6.0.23", "0.6")]
    [InlineData("v0.6.0-rc.4", "0.6")]
    [InlineData("0.5.11", "0.5")]
    [InlineData("1", "1")]
    public void Minor_drops_the_patch_build_and_prerelease_parts(string version, string minor) =>
        Assert.Equal(minor, ReleaseNotes.Minor(version));

    [Fact]
    public void Group_keeps_the_file_order_and_puts_builds_under_their_minor()
    {
        var groups = ReleaseNotes.Group(ReleaseNotes.Parse(Sample));

        Assert.Equal(["0.6", "0.5", "0.6"], groups.Select(g => g.Minor));
        Assert.Equal([false, false, true], groups.Select(g => g.Legacy));
        Assert.Equal(["0.6.0.2", "0.6.0"], groups[0].Entries.Select(e => e.Version));
        Assert.Equal(["0.6.0"], groups[2].Entries.Select(e => e.Version));
    }

    [Fact]
    public void Rows_show_a_title_per_minor_then_each_version_and_its_bullets()
    {
        var rows = ReleaseNotesRows.For(ReleaseNotes.Group(ReleaseNotes.Parse(Sample)))
            .Select(r => r.Text)
            .ToList();

        Assert.Equal("v0.6", rows[0]);
        Assert.Equal("0.6.0.2   2026-10-02", rows[2]);
        Assert.Equal("  • Remotes have nicknames.", rows[3]);
        Assert.Contains("v0.5", rows);
        Assert.Contains("0.5.24", rows);
        Assert.Equal("v0.6" + ReleaseNotesRows.LegacyLabel, rows[^4]);
    }

    [Fact]
    public void Rows_show_a_hint_when_there_are_no_notes() =>
        Assert.Equal(ReleaseNotesRows.EmptyHint, ReleaseNotesRows.For([]).Single().Text);

    [Fact]
    public void Wrap_breaks_long_bullets_on_word_boundaries()
    {
        var lines = ReleaseNotesRows.Wrap("one two three four five", 9);

        Assert.Equal(["one two", "three", "four five"], lines);
        Assert.Equal([string.Empty], ReleaseNotesRows.Wrap(string.Empty));
    }

    [Fact]
    public void The_embedded_notes_parse_and_every_entry_is_short_and_unique()
    {
        var entries = ReleaseNotes.Parse(ReleaseNotes.Embedded());

        Assert.NotEmpty(entries);
        Assert.Equal(entries.Count, entries.Select(e => (e.Version, e.Legacy)).Distinct().Count());
        Assert.Contains(entries, e => !e.Legacy);
        Assert.Contains(entries, e => e.Legacy);
        Assert.All(entries, e =>
        {
            Assert.Matches(VersionPattern(), e.Version);
            Assert.Matches(DatePattern(), e.Date ?? string.Empty);
            Assert.InRange(e.Bullets.Count, 1, 6);
        });
    }

    [Fact]
    public void The_embedded_notes_are_the_repo_file()
    {
        var file = File.ReadAllText(Path.Combine(RepoRoot, ReleaseNotes.ResourceName));

        Assert.Equal(
            ReleaseNotes.Parse(file).Select(e => e.Version),
            ReleaseNotes.Parse(ReleaseNotes.Embedded()).Select(e => e.Version));
    }

    // Until the version restart lands, Fleet.csproj still holds the last old build,
    // which only has an entry under the earlier builds.
    [Fact]
    public void The_version_in_the_project_file_has_an_entry()
    {
        var csproj = File.ReadAllText(Path.Combine(RepoRoot, "src", "Fleet", "Fleet.csproj"));
        var version = ProjectVersion().Match(csproj).Groups[1].Value;
        var entries = ReleaseNotes.Parse(ReleaseNotes.Embedded());

        Assert.True(
            entries.Any(e => !e.Legacy && e.Version == version)
                || entries.First(e => e.Legacy).Version == version,
            $"RELEASE_NOTES.md has no entry for {version}");
    }

    [GeneratedRegex(@"^\d+\.\d+\.\d+(\.\d+)?$")]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$")]
    private static partial Regex DatePattern();

    [GeneratedRegex("<Version>([^<]+)</Version>")]
    private static partial Regex ProjectVersion();
}
