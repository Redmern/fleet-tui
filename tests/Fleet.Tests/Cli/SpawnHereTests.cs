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
    public async Task A_multiplexer_without_floats_opens_a_normal_pane_as_before()
    {
        var mux = new FakeMuxDriver();
        mux.CurrentPane = await mux.SpawnAsync(new SpawnOptions { SessionName = "techweb", Args = ["fleet", "menu"] });

        await Adapters.SpawnHereAsync(mux, Browser("techweb"));

        Assert.DoesNotContain("spawn-float", mux.Calls);
    }
}
