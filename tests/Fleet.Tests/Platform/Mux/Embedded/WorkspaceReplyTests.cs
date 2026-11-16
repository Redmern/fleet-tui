using System.Text.Json;
using Fleet.Platform.Mux.Embedded;
using Fleet.Platform.Mux.Embedded.Protocol;

namespace Fleet.Tests.Platform.Mux.Embedded;

public sealed class WorkspaceReplyTests
{
    private static Fleet.Ports.Mux.Models.Workspace Read(string json) =>
        EmbeddedDriver.ToWorkspace(
            JsonSerializer.Deserialize(json, WireJsonContext.Default.ControlResponse)!.Workspaces!.Single());

    [Fact]
    public void A_reply_from_a_daemon_that_predates_the_field_reads_as_unknown_not_as_nobody()
    {
        var workspace = Read("""{"ok":true,"workspaces":[{"name":"techweb","shownHere":true,"inWindow":true,"inOtherWindow":false}]}""");

        Assert.Null(workspace.ShownAnywhere);
        Assert.True(workspace.ShownHere);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void A_reply_that_carries_the_field_is_read_as_sent(string sent, bool expected)
    {
        var workspace = Read($$"""{"ok":true,"workspaces":[{"name":"techweb","shownAnywhere":{{sent}}}]}""");

        Assert.Equal(expected, workspace.ShownAnywhere);
    }
}
