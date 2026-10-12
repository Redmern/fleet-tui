using Fleet.Platform.Mux.Embedded;
using Fleet.Platform.Mux.Embedded.Protocol;
using Fleet.Ports.Remotes.Models;

namespace Fleet.Tests.Platform.Mux.Embedded;

public sealed partial class RemoteLinkTests
{
    [Fact]
    public async Task A_project_created_for_a_remote_carries_its_repositories_to_that_remotes_fleet()
    {
        using var home = await ConnectedHomeAsync();
        var remotes = new EmbeddedRemotes(() => new EmbeddedDriver(_home));

        var made = await remotes.CreateProjectAsync(
            "red@far", new RemoteProjectRequest("shop", "/srv/shop", false, true, ["api", "web"]));

        Assert.False(made.IsError, made.Text);
        var (machine, creation) = Assert.Single(_creations);
        Assert.Equal("far", machine);
        Assert.Equal(["api", "web"], creation.Repositories ?? []);
    }

    [Fact]
    public void Project_creation_wire_round_trips_repositories()
    {
        var sent = new RemoteProjectRequest("shop", "/srv/shop", true, true, ["api", "web"]);

        var received = ProjectCreationWire.Creation(ProjectCreationWire.Request("create-project", null, sent));

        Assert.Equal(["api", "web"], received.Repositories ?? []);
        Assert.Equal(sent with { Repositories = received.Repositories }, received);
    }

    [Fact]
    public void Project_creation_wire_without_repositories_reads_back_none()
    {
        var sent = new RemoteProjectRequest("shop", "/srv/shop", false, true);

        var received = ProjectCreationWire.Creation(ProjectCreationWire.Request("create-project", null, sent));

        Assert.Equal(sent, received);
        Assert.Null(received.Repositories);
    }
}
