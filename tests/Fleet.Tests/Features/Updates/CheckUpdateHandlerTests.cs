using Fleet.Features.Updates.CheckUpdate;
using Fleet.Platform.Releases.Fake;
using Fleet.Ports.Releases.Models;

namespace Fleet.Tests.Features.Updates;

public class CheckUpdateHandlerTests
{
    [Fact]
    public async Task Reports_an_update_when_the_release_is_newer()
    {
        var client = new FakeReleaseClient { Release = new ReleaseInfo("v0.2.0", []) };

        var check = await new CheckUpdateHandler(client).HandleAsync("owner/repo", "0.1.0");

        Assert.True(check.UpdateAvailable);
        Assert.Equal("v0.2.0", check.Latest);
        Assert.Null(check.Error);
    }

    [Fact]
    public async Task Reports_up_to_date_when_the_release_matches()
    {
        var client = new FakeReleaseClient { Release = new ReleaseInfo("v0.1.0", []) };

        var check = await new CheckUpdateHandler(client).HandleAsync("owner/repo", "0.1.0");

        Assert.False(check.UpdateAvailable);
    }

    [Fact]
    public async Task Reports_an_error_when_no_release_is_found()
    {
        var client = new FakeReleaseClient { Release = null };

        var check = await new CheckUpdateHandler(client).HandleAsync("owner/repo", "0.1.0");

        Assert.False(check.UpdateAvailable);
        Assert.Contains("owner/repo", check.Error);
    }
}
