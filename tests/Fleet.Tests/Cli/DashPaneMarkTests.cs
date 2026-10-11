using Fleet.Cli.Commands;
using Fleet.Cli.Composition;
using Fleet.Ports.Mux.Models;

namespace Fleet.Tests.Cli;

[Collection(ConfigHomeCollection.Name)]
public sealed class DashPaneMarkTests : ConfigHomeFixture
{
    private const string Root = "C:/repos/techweb";

    private static Pane At(string id) =>
        new(new PaneId(id), "techweb", "t1", "techweb", "dash", Root, false);

    // Go to dashboard landed on claude because nothing wrote the marker once
    // the wezterm driver was removed: the dashboard must mark its own pane.
    [Fact]
    public void A_dashboard_that_marks_its_pane_is_where_go_to_dashboard_lands()
    {
        Pane[] panes = [At("1"), At("2")];

        Adapters.MarkDashboardPane("techweb", "2");

        Assert.Equal("2", MenuCommand.DashboardPane(panes, Root, Adapters.DashPane("techweb"))!.Id.Value);
    }

    [Fact]
    public void Outside_the_embedded_mux_no_marker_is_written()
    {
        Adapters.MarkDashboardPane("techweb", null);

        Assert.Null(Adapters.DashPane("techweb"));
    }
}
