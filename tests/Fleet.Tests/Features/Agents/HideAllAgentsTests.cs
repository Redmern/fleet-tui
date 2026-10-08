using Fleet.Features.Agents.HideAgent;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;

namespace Fleet.Tests.Features.Agents;

public class HideAllAgentsTests
{
    private readonly FakeMuxDriver _mux = new();

    private readonly KeyedStore _store = new();

    private static AgentRecord Agent(string branch, string harness = AgentHarness.Claude, bool hidden = false) =>
        new($"C:/repos/techweb/backend/{branch}", "backend", branch, harness, "origin/main", true, hidden);

    [Fact]
    public async Task Every_shown_agent_and_sub_orchestrator_moves_into_the_hidden_workspace()
    {
        var agent = Agent("one");
        var sub = Agent("sub", AgentHarness.Orchestrator);
        _store.Save("techweb", agent);
        _store.Save("techweb", sub);
        var agentPane = await _mux.SpawnAsync(new SpawnOptions { Cwd = agent.Worktree });
        var subPane = await _mux.SpawnAsync(new SpawnOptions { Cwd = sub.Worktree });

        var hidden = await new HideAllAgentsHandler(_mux, _store).HandleAsync("techweb");

        var panes = await _mux.ListPanesAsync();
        Assert.Equal(2, hidden);
        Assert.Equal(FleetWorkspaces.Hidden, panes.Single(p => p.Id == agentPane).SessionName);
        Assert.Equal(FleetWorkspaces.Hidden, panes.Single(p => p.Id == subPane).SessionName);
        Assert.All(_store.List("techweb"), a => Assert.True(a.Hidden));
    }

    // The per-agent hide toggles, so hiding all must never bring back an agent
    // that is already hidden.
    [Fact]
    public async Task An_already_hidden_agent_stays_hidden()
    {
        var shown = Agent("shown");
        var hidden = Agent("hidden");
        _store.Save("techweb", shown);
        _store.Save("techweb", hidden);
        await _mux.SpawnAsync(new SpawnOptions { Cwd = shown.Worktree });
        var hiddenPane = await _mux.SpawnAsync(new SpawnOptions { Cwd = hidden.Worktree });
        await new HideAgentHandler(_mux, _store).HandleAsync("techweb", hidden, "w1");

        var count = await new HideAllAgentsHandler(_mux, _store).HandleAsync("techweb");

        Assert.Equal(1, count);
        Assert.Equal(
            FleetWorkspaces.Hidden,
            (await _mux.ListPanesAsync()).Single(p => p.Id == hiddenPane).SessionName);
    }

    [Fact]
    public async Task An_agent_without_a_pane_is_left_alone()
    {
        _store.Save("techweb", Agent("closed") with { Open = false });

        var count = await new HideAllAgentsHandler(_mux, _store).HandleAsync("techweb");

        Assert.Equal(0, count);
        Assert.False(Assert.Single(_store.List("techweb")).Hidden);
    }

    [Fact]
    public void The_summary_says_how_many_were_hidden()
    {
        Assert.Equal("no agents are showing in the terminal.", HideAllAgentsHandler.Summary(0));
        Assert.StartsWith("hid 3 agent(s)", HideAllAgentsHandler.Summary(3), StringComparison.Ordinal);
    }

    private sealed class KeyedStore : IAgentStore
    {
        private readonly List<AgentRecord> _agents = [];

        public void Save(string project, AgentRecord agent)
        {
            _agents.RemoveAll(a => a.Worktree == agent.Worktree);
            _agents.Add(agent);
        }

        public IReadOnlyList<AgentRecord> List(string project) => _agents;

        public void Remove(string project, string worktree) =>
            _agents.RemoveAll(a => a.Worktree == worktree);
    }
}
