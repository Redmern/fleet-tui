using Fleet.Features.Agents;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux.Models;
using Fleet.Shared.Constants;

namespace Fleet.Tests.Features.Agents;

public class HiddenNestTests
{
    private readonly FakeMuxDriver _mux = new();

    private static AgentRecord Agent(string worktree) =>
        new(worktree, "backend", "test", AgentHarness.Claude, "origin/main", true);

    [Fact]
    public async Task Moving_into_a_project_specific_hidden_workspace_uses_that_name()
    {
        var agent = await _mux.SpawnAsync(new SpawnOptions { Cwd = "/repos/backend/worktree" });

        await HiddenNest.MoveIntoAsync(_mux, [agent], null, "fleet-hidden-proj-a");

        var pane = (await _mux.ListPanesAsync()).Single(p => p.Id == agent);
        Assert.Equal("fleet-hidden-proj-a", pane.SessionName);
    }

    [Fact]
    public async Task A_second_pane_joins_the_first_ones_window_in_that_same_hidden_workspace()
    {
        var first = await _mux.SpawnAsync(new SpawnOptions { Cwd = "/repos/backend/a" });
        var second = await _mux.SpawnAsync(new SpawnOptions { Cwd = "/repos/backend/b" });

        var hiddenWindow = await HiddenNest.MoveIntoAsync(_mux, [first], null, "fleet-hidden-proj-a");
        await HiddenNest.MoveIntoAsync(_mux, [second], hiddenWindow, "fleet-hidden-proj-a");

        var panes = await _mux.ListPanesAsync();
        var one = panes.Single(p => p.Id == first);
        var two = panes.Single(p => p.Id == second);

        Assert.Equal("fleet-hidden-proj-a", one.SessionName);
        Assert.Equal("fleet-hidden-proj-a", two.SessionName);
        Assert.Equal(one.WindowId, two.WindowId);
    }

    [Fact]
    public void WindowOf_only_matches_the_given_hidden_workspace_name()
    {
        var agent = Agent("/repos/backend/a");
        var panes = new[]
        {
            new Pane(new PaneId("1"), "w1", "t1", "fleet-hidden-proj-a", "", agent.Worktree, false),
            new Pane(new PaneId("2"), "w2", "t2", "fleet-hidden-proj-b", "", agent.Worktree, false),
        };

        Assert.Equal("w1", HiddenNest.WindowOf(panes, [agent], "fleet-hidden-proj-a"));
        Assert.Null(HiddenNest.WindowOf(panes, [agent], "fleet-hidden-proj-c"));
    }
}
