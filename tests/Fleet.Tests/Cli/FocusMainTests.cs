using Fleet.Cli.Commands;
using Fleet.Ports.Mux.Models;
using Fleet.Shared.Constants;

namespace Fleet.Tests.Cli;

public class FocusMainTests
{
    private const string Root = "C:/repos/techweb";

    private static Pane At(string id, string cwd = Root, string session = "techweb") =>
        new(new PaneId(id), "w1", "t1", session, "dashboard", cwd, false);

    // The main orchestrator's claude pane shares the project root with the
    // dashboard and often comes first; "m" must land on the dashboard.
    [Fact]
    public void The_marked_dashboard_wins_over_claude_in_the_same_folder()
    {
        Pane[] panes = [At("1"), At("2")];

        Assert.Equal("2", MenuCommand.DashboardPane(panes, Root, "2")!.Id.Value);
    }

    [Fact]
    public void Without_a_marker_the_first_shown_pane_in_the_root_is_used()
    {
        Pane[] panes = [At("1", session: FleetWorkspaces.Hidden), At("2", cwd: "C:/elsewhere"), At("3")];

        Assert.Equal("3", MenuCommand.DashboardPane(panes, Root, null)!.Id.Value);
    }

    [Fact]
    public void A_stale_marker_falls_back_to_the_root_pane()
    {
        Pane[] panes = [At("1"), At("9", cwd: "C:/elsewhere")];

        Assert.Equal("1", MenuCommand.DashboardPane(panes, Root, "9")!.Id.Value);
        Assert.Equal("1", MenuCommand.DashboardPane(panes, Root, "42")!.Id.Value);
    }
}
