using Fleet.Platform.Mux.Embedded.Protocol;
using Fleet.Ports.Remotes.Models;

namespace Fleet.Tests.Platform.Mux.Embedded;

public sealed class ProjectCreationWireTests
{
    [Fact]
    public void An_interactive_creation_round_trips()
    {
        var sent = new RemoteProjectRequest("shop", "/srv/shop", true, true, Interactive: true);

        var received = ProjectCreationWire.Creation(ProjectCreationWire.Request("create-project", null, sent));

        Assert.True(received.Interactive);
        Assert.Equal(sent, received);
    }

    [Fact]
    public void A_request_without_the_interactive_entry_is_not_interactive()
    {
        var request = ProjectCreationWire.Request("create-project", null, new RemoteProjectRequest("shop", "/srv/shop", false, true));
        request.Env!.Remove(ProjectCreationWire.Interactive);

        Assert.False(ProjectCreationWire.Creation(request).Interactive);
    }
}
