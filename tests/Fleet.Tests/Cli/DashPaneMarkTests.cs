using Fleet.Cli.Commands;
using Fleet.Cli.Composition;
using Fleet.Platform.Storage;
using Fleet.Ports.Mux.Models;

namespace Fleet.Tests.Cli;

[Collection(ConfigHomeCollection.Name)]
public sealed class DashPaneMarkTests : ConfigHomeFixture
{
    private const string Root = "C:/repos/techweb";

    private static Pane At(string id) =>
        new(new PaneId(id), "techweb", "t1", "techweb", "dash", Root, false);

    // Go to dashboard landed on claude because nothing wrote the marker once
    // the wezterm driver was removed. DashCommand writes it on start; that call
    // itself runs inside the Terminal.Gui dashboard and is not unit tested here.
    [Fact]
    public void A_written_marker_is_where_go_to_dashboard_lands()
    {
        Pane[] panes = [At("1"), At("2")];

        DashPaneMarker.Write("techweb", "2");

        Assert.Equal("2", MenuCommand.DashboardPane(panes, Root, Adapters.DashPane("techweb"))!.Id.Value);
    }

    [Fact]
    public void Without_a_pane_id_no_marker_is_written()
    {
        DashPaneMarker.Write("techweb", null);

        Assert.Null(Adapters.DashPane("techweb"));
    }
}
