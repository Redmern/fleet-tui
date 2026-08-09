using Fleet.Features.Agents.ChangeHarness;
using Fleet.Features.Agents.HideAgent;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux.Models;
using Fleet.Shared.Constants;

namespace Fleet.Tests.Features.Agents;

public class HideAgentTests
{
    private readonly FakeMuxDriver _mux = new();

    private readonly RecordingStore _store = new();

    private static AgentRecord Agent(bool hidden = false) =>
        new("C:/repos/techweb/backend/test", "backend", "test", AgentHarness.Claude,
            "origin/main", true, hidden);

    [Fact]
    public async Task Hiding_moves_the_pane_into_the_hidden_workspace()
    {
        var agent = Agent();
        var pane = await _mux.SpawnAsync(new SpawnOptions { Cwd = agent.Worktree });

        var result = await new HideAgentHandler(_mux, _store)
            .HandleAsync("techweb", agent, dashboardWindow: "w1");

        Assert.True(result.Succeeded, result.Error);
        Assert.True(result.Value!.Hidden);

        var panes = await _mux.ListPanesAsync();
        Assert.Equal(FleetWorkspaces.Hidden, panes.Single(p => p.Id == pane).SessionName);
    }

    [Fact]
    public async Task A_hidden_agent_is_still_recorded_so_the_dashboard_keeps_listing_it()
    {
        var agent = Agent();
        await _mux.SpawnAsync(new SpawnOptions { Cwd = agent.Worktree });

        await new HideAgentHandler(_mux, _store).HandleAsync("techweb", agent, "w1");

        var saved = Assert.Single(_store.Saved);

        Assert.True(saved.Hidden);
        Assert.Equal(agent.Worktree, saved.Worktree);
    }

    [Fact]
    public async Task Showing_a_hidden_agent_moves_it_back_to_the_dashboards_window()
    {
        var agent = Agent(hidden: true);
        var pane = await _mux.SpawnAsync(
            new SpawnOptions { Cwd = agent.Worktree, Workspace = FleetWorkspaces.Hidden });

        var result = await new HideAgentHandler(_mux, _store)
            .HandleAsync("techweb", agent, dashboardWindow: "w9");

        Assert.True(result.Succeeded, result.Error);
        Assert.False(result.Value!.Hidden);

        var panes = await _mux.ListPanesAsync();
        Assert.Equal("w9", panes.Single(p => p.Id == pane).WindowId);
    }

    [Fact]
    public async Task An_agent_with_no_pane_still_records_the_change()
    {
        var result = await new HideAgentHandler(_mux, _store)
            .HandleAsync("techweb", Agent(), "w1");

        Assert.True(result.Succeeded, result.Error);
        Assert.True(result.Value!.Hidden);
        Assert.Single(_store.Saved);
    }

    [Fact]
    public void Changing_the_harness_records_it_against_the_same_agent()
    {
        var changed = new ChangeHarnessHandler(_store)
            .Handle("techweb", Agent(), AgentHarness.Nvim);

        Assert.True(changed.Succeeded, changed.Error);
        Assert.Equal(AgentHarness.Nvim, changed.Value!.Harness);
        Assert.Equal(AgentHarness.Nvim, Assert.Single(_store.Saved).Harness);
    }

    [Fact]
    public void Choosing_the_harness_it_already_has_writes_nothing()
    {
        var changed = new ChangeHarnessHandler(_store)
            .Handle("techweb", Agent(), AgentHarness.Claude);

        Assert.True(changed.Succeeded, changed.Error);
        Assert.Empty(_store.Saved);
    }

    [Fact]
    public void An_unknown_harness_falls_back_to_claude_rather_than_being_stored()
    {
        var changed = new ChangeHarnessHandler(_store)
            .Handle("techweb", Agent() with { Harness = AgentHarness.Nvim }, "emacs");

        Assert.Equal(AgentHarness.Claude, changed.Value!.Harness);
    }

    private sealed class RecordingStore : IAgentStore
    {
        public List<AgentRecord> Saved { get; } = [];

        public void Save(string project, AgentRecord agent) => Saved.Add(agent);

        public IReadOnlyList<AgentRecord> List(string project) => Saved;

        public void Remove(string project, string worktree) { }
    }
}
