using Fleet.Cli.Composition;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Mux.Models;

namespace Fleet.Tests.Cli;

public class SpawnHereTests
{
    private static SpawnOptions Browser(string session) =>
        new() { SessionName = session, WindowId = "w9", Cwd = "C:/repos/techweb", Args = ["yazi"] };

    [Fact]
    public async Task Inside_a_multiplexer_with_floats_it_opens_a_float_in_the_callers_workspace()
    {
        var mux = new FakeMuxDriver(workspaces: true);
        mux.CurrentPane = await mux.SpawnAsync(new SpawnOptions { SessionName = "techweb", Args = ["fleet", "menu"] });

        var pane = await Adapters.SpawnHereAsync(mux, Browser("some-new-project"));

        var opened = (await mux.ListPanesAsync()).Single(p => p.Id == pane);
        Assert.Equal(("techweb", "float"), (opened.SessionName, opened.TabId));
        Assert.Equal(["yazi"], mux.ArgsFor(pane));
        Assert.Contains("spawn-float", mux.Calls);
    }

    [Fact]
    public async Task Outside_any_pane_it_opens_a_normal_pane_as_before()
    {
        var mux = new FakeMuxDriver(workspaces: true);

        await Adapters.SpawnHereAsync(mux, Browser("techweb"));

        Assert.DoesNotContain("spawn-float", mux.Calls);
        Assert.Contains("spawn", mux.Calls);
    }

    [Fact]
    public void Outside_any_pane_of_a_workspace_multiplexer_a_spawned_pane_is_shown_nowhere()
    {
        // `fleet` run from a plain terminal on the embedded mux: yazi would open in a workspace
        // no client shows, and the folder picker would block its UI waiting for it.
        Assert.False(Adapters.CanShowPaneHere(new FakeMuxDriver(workspaces: true)));
    }

    [Fact]
    public async Task Inside_a_pane_of_a_workspace_multiplexer_a_spawned_pane_is_shown()
    {
        var mux = new FakeMuxDriver(workspaces: true);
        mux.CurrentPane = await mux.SpawnAsync(new SpawnOptions { SessionName = "techweb", Args = ["fleet", "menu"] });

        Assert.True(Adapters.CanShowPaneHere(mux));
    }

    [Fact]
    public void A_multiplexer_without_workspaces_shows_a_spawned_pane_from_anywhere()
    {
        Assert.True(Adapters.CanShowPaneHere(new FakeMuxDriver()));
    }

    [Fact]
    public async Task A_multiplexer_without_floats_opens_a_normal_pane_as_before()
    {
        var mux = new FakeMuxDriver();
        mux.CurrentPane = await mux.SpawnAsync(new SpawnOptions { SessionName = "techweb", Args = ["fleet", "menu"] });

        await Adapters.SpawnHereAsync(mux, Browser("techweb"));

        Assert.DoesNotContain("spawn-float", mux.Calls);
    }
}
