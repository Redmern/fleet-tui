using Fleet.Features.Agents;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Mux.Models;
using Fleet.Shared.Constants;

namespace Fleet.Tests.Features.Agents;

public class HiddenNestTests
{
    private readonly FakeMuxDriver _mux = new();

    [Fact]
    public async Task Moving_into_the_background_keeps_the_same_window_and_workspace()
    {
        var dash = await _mux.SpawnAsync(new SpawnOptions { Cwd = "/repos/backend" });
        var window = (await _mux.ListPanesAsync()).Single(p => p.Id == dash).WindowId;
        var agent = await _mux.SpawnAsync(new SpawnOptions { Cwd = "/repos/backend/worktree" });

        await HiddenNest.MoveIntoBackgroundAsync(_mux, [agent], window);

        var panes = await _mux.ListPanesAsync();
        var dashPane = panes.Single(p => p.Id == dash);
        var agentPane = panes.Single(p => p.Id == agent);

        Assert.Equal(window, agentPane.WindowId);
        Assert.Equal(dashPane.SessionName, agentPane.SessionName);
        Assert.NotEqual(FleetWorkspaces.Hidden, agentPane.SessionName);
    }

    [Fact]
    public async Task Moving_into_the_background_gives_each_pane_its_own_tab()
    {
        var dash = await _mux.SpawnAsync(new SpawnOptions { Cwd = "/repos/backend" });
        var window = (await _mux.ListPanesAsync()).Single(p => p.Id == dash).WindowId;
        var agent = await _mux.SpawnAsync(new SpawnOptions { Cwd = "/repos/backend/worktree" });
        var before = (await _mux.ListPanesAsync()).Single(p => p.Id == agent).TabId;

        await HiddenNest.MoveIntoBackgroundAsync(_mux, [agent], window);

        var after = (await _mux.ListPanesAsync()).Single(p => p.Id == agent).TabId;
        Assert.NotEqual(before, after);
    }
}
