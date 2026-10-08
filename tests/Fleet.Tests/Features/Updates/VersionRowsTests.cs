using Fleet.Features.Updates.ShowVersion;
using Fleet.Features.Updates.ShowVersion.Models;
using Fleet.Ports.Releases.Models;

namespace Fleet.Tests.Features.Updates;

public class VersionRowsTests
{
    private static readonly IReadOnlyList<ReleaseInfo> Releases =
    [
        new("v0.6.0.27", []),
        new("v0.6.0.26", []),
        new("v0.6.0-rc.1", [], Prerelease: true),
    ];

    [Fact]
    public void The_summary_shows_installed_and_latest_and_says_an_update_is_available()
    {
        var summary = VersionRows.Summary(new VersionScreen("0.6.0.26", "v0.6.0.27", true, Releases));

        Assert.Equal("Installed  v0.6.0.26", summary[0]);
        Assert.Equal("Latest     v0.6.0.27, an update is available", summary[1]);
    }

    [Fact]
    public void The_summary_says_up_to_date_or_unknown()
    {
        Assert.Contains("up to date", VersionRows.Summary(new VersionScreen("0.6.0.27", "v0.6.0.27", false, Releases))[1]);
        Assert.Contains("unknown", VersionRows.Summary(new VersionScreen("0.6.0.27", null, false, []))[1]);
    }

    [Fact]
    public void Each_release_is_a_row_marked_installed_latest_or_prerelease()
    {
        var rows = VersionRows.Rows(new VersionScreen("0.6.0.26", "v0.6.0.27", true, Releases));

        Assert.Equal(3, rows.Count);
        Assert.Contains("latest", rows[0].Text);
        Assert.Contains("installed", rows[1].Text);
        Assert.Contains("prerelease", rows[2].Text);
        Assert.DoesNotContain("installed", rows[0].Text);
    }

    [Fact]
    public void No_releases_means_no_rows() =>
        Assert.Empty(VersionRows.Rows(new VersionScreen("0.6.0.26", null, false, [])));
}
