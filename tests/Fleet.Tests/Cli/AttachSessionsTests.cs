using Fleet.Cli.Composition;
using Fleet.Ports.Mux.Models;
using Fleet.Shared.Constants;

namespace Fleet.Tests.Cli;

public class AttachSessionsTests
{
    private static Pane In(string workspace, string id) =>
        new(new PaneId(id), workspace, "t1", workspace, string.Empty, "C:/x", true);

    [Fact]
    public void Sessions_are_the_project_workspaces_with_their_pane_counts_sorted_by_name()
    {
        var sessions = EmbeddedWiring.Sessions(
            [new Workspace("techweb", false), new Workspace("fleet", false), new Workspace("techweb" + FleetWorkspaces.HiddenSuffix, false)],
            [In("techweb", "p1"), In("techweb", "p2"), In("fleet", "p3"), In("techweb" + FleetWorkspaces.HiddenSuffix, "p4")]);

        Assert.Equal([("fleet", 1), ("techweb", 2)], sessions);
    }
}