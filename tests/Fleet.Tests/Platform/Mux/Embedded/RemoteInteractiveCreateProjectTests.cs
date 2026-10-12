using Fleet.Platform.Mux.Embedded;
using Fleet.Platform.Mux.Embedded.Protocol;
using Fleet.Ports.Remotes.Models;

namespace Fleet.Tests.Platform.Mux.Embedded;

public sealed partial class RemoteLinkTests
{
    [Fact]
    public async Task An_interactive_create_reaches_the_remotes_fleet_as_interactive()
    {
        using var home = await ConnectedHomeAsync();
        var remotes = new EmbeddedRemotes(() => new EmbeddedDriver(_home));

        var made = await remotes.CreateProjectAsync(
            "red@far", new RemoteProjectRequest("shop", "/srv/shop", false, true, Interactive: true));

        Assert.False(made.IsError, made.Text);
        Assert.True(Assert.Single(_creations).Creation.Interactive);
    }
}
