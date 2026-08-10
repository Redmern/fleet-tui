using Fleet.Features.Projects.QuitProject;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux.Models;
using Fleet.Shared.Constants;

namespace Fleet.Tests.Features.Projects;

public class QuitProjectTests
{
    private static Pane Pane(string id, string window, string cwd) =>
        new(new PaneId(id), window, TabId: id, SessionName: "default", Title: "", Cwd: cwd,
            IsActive: false);

    private static AgentRecord Agent(string worktree) =>
        new(worktree, "backend", "dev", AgentHarness.Claude, "main", true, Hidden: true);

    [Fact]
    public void Every_pane_in_the_projects_window_is_closed()
    {
        var doomed = QuitPlan.PanesToClose(
            [
                Pane("1", "w1", "C:/repos/techweb"),
                Pane("2", "w1", "C:/repos/techweb/backend/dev"),
            ],
            projectWindow: "w1",
            agents: []);

        Assert.Equal([new PaneId("1"), new PaneId("2")], doomed);
    }

    [Fact]
    public void Another_windows_panes_are_left_alone()
    {
        var doomed = QuitPlan.PanesToClose(
            [Pane("1", "w1", "C:/repos/techweb"), Pane("9", "w2", "C:/repos/other")],
            projectWindow: "w1",
            agents: []);

        Assert.Equal([new PaneId("1")], doomed);
    }

    [Fact]
    public void A_hidden_agent_in_another_window_is_closed_too_so_nothing_is_orphaned()
    {
        var doomed = QuitPlan.PanesToClose(
            [
                Pane("1", "w1", "C:/repos/techweb"),
                Pane("7", "w5", "C:/repos/techweb/backend/dev"),
            ],
            projectWindow: "w1",
            agents: [Agent("C:/repos/techweb/backend/dev")]);

        Assert.Equal([new PaneId("1"), new PaneId("7")], doomed);
    }

    [Fact]
    public void A_pane_that_is_both_in_the_window_and_an_agent_is_only_closed_once()
    {
        var doomed = QuitPlan.PanesToClose(
            [Pane("1", "w1", "C:/repos/techweb/backend/dev")],
            projectWindow: "w1",
            agents: [Agent("C:/repos/techweb/backend/dev")]);

        Assert.Single(doomed);
    }

    [Fact]
    public void With_no_dashboard_pane_only_the_agents_are_closed()
    {
        var doomed = QuitPlan.PanesToClose(
            [Pane("3", "w4", "C:/repos/techweb/backend/dev"), Pane("4", "w4", "C:/elsewhere")],
            projectWindow: null,
            agents: [Agent("C:/repos/techweb/backend/dev")]);

        Assert.Equal([new PaneId("3")], doomed);
    }

    [Fact]
    public async Task Nothing_open_is_reported_rather_than_silently_succeeding()
    {
        var result = await new QuitProjectHandler(new FakeMuxDriver())
            .HandleAsync("C:/repos/techweb", []);

        Assert.False(result.Succeeded);
        Assert.Contains("Nothing", result.Error);
    }
}
