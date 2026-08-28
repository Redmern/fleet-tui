using Fleet.Features.Projects.QuitProject;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Agents;
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
    public void A_hidden_nvim_pane_is_matched_by_its_tab_title_when_the_cwd_lies()
    {
        var hidden = Pane("7", "w5", cwd: "C:/somewhere/else") with
        {
            Title = "backend/dev",
            SessionName = "fleet-hidden",
        };

        var doomed = QuitPlan.PanesToClose(
            [Pane("1", "w1", "C:/repos/techweb"), hidden],
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
        var result = await new QuitProjectHandler(new FakeMuxDriver(), new RecordingStore())
            .HandleAsync("techweb", "C:/repos/techweb", []);

        Assert.False(result.Succeeded);
        Assert.Contains("Nothing", result.Error);
    }

    [Fact]
    public async Task Quitting_remembers_which_agents_were_open_and_which_were_not()
    {
        var mux = new FakeMuxDriver();
        var store = new RecordingStore();

        var root = Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());
        var live = Path.Combine(root, "backend", "dev");
        var idle = Path.Combine(root, "backend", "spike");

        await mux.SpawnAsync(new SpawnOptions { Cwd = root });
        await mux.SpawnAsync(new SpawnOptions { Cwd = live });

        var agents = new[] { Agent(live), Agent(idle) with { Open = true } };

        var result = await new QuitProjectHandler(mux, store).HandleAsync("techweb", root, agents);

        Assert.True(result.Succeeded, result.Error);

        Assert.Equal(
            [(live, true), (idle, false)],
            store.Saved.Select(a => (a.Worktree, a.Open)));
    }

    [Fact]
    public async Task An_agent_whose_state_did_not_change_is_not_rewritten()
    {
        var mux = new FakeMuxDriver();
        var store = new RecordingStore();

        var root = Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());
        var live = Path.Combine(root, "backend", "dev");

        await mux.SpawnAsync(new SpawnOptions { Cwd = root });
        await mux.SpawnAsync(new SpawnOptions { Cwd = live });

        await new QuitProjectHandler(mux, store)
            .HandleAsync("techweb", root, [Agent(live) with { Open = true }]);

        Assert.Empty(store.Saved);
    }

    private sealed class RecordingStore : IAgentStore
    {
        public List<AgentRecord> Saved { get; } = [];

        public void Save(string project, AgentRecord agent) => Saved.Add(agent);

        public IReadOnlyList<AgentRecord> List(string project) => Saved;

        public void Remove(string project, string worktree) { }
    }
}
