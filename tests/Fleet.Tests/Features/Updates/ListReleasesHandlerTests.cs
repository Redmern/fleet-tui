using Fleet.Features.Updates.ListReleases;
using Fleet.Platform.Releases.Fake;
using Fleet.Ports.Releases.Models;

namespace Fleet.Tests.Features.Updates;

public class ListReleasesHandlerTests
{
    [Fact]
    public async Task Returns_whatever_the_release_client_lists()
    {
        var client = new FakeReleaseClient
        {
            List =
            [
                new ReleaseInfo("v0.5.1", []),
                new ReleaseInfo("v0.5.0", []),
                new ReleaseInfo("v0.4.1-beta", [], Prerelease: true),
            ],
        };

        var releases = await new ListReleasesHandler(client).HandleAsync("owner/repo");

        Assert.Equal(3, releases.Count);
        Assert.Equal("v0.5.1", releases[0].Tag);
        Assert.True(releases[2].Prerelease);
    }

    [Fact]
    public async Task An_empty_list_is_not_an_error()
    {
        var releases = await new ListReleasesHandler(new FakeReleaseClient()).HandleAsync("owner/repo");

        Assert.Empty(releases);
    }
}
