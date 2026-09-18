using Fleet.Platform.Mux;
using Fleet.Platform.Mux.Fake;
using Fleet.Platform.Mux.WezTerm;
using Fleet.Ports.Mux.Models;

namespace Fleet.Tests.Platform.Mux;

public class AutoStartDriverTests
{
    [Fact]
    public async Task Ensures_the_instance_is_running_exactly_once_across_many_calls()
    {
        var ensureCalls = 0;

        var launcher = new WezTermInstanceLauncher(
            reachable: _ =>
            {
                ensureCalls++;
                return Task.FromResult(true);
            },
            start: _ => { });

        var driver = new AutoStartDriver(
            new FakeMuxDriver(), launcher, "config.lua", TimeSpan.FromSeconds(1));

        await driver.ListPanesAsync();
        await driver.ListPanesAsync();
        await driver.SpawnAsync(new SpawnOptions { Cwd = "/x" });

        Assert.Equal(1, ensureCalls);
    }

    [Fact]
    public async Task A_launcher_that_never_becomes_reachable_does_not_block_the_call()
    {
        var driver = new AutoStartDriver(
            new FakeMuxDriver(),
            new WezTermInstanceLauncher(reachable: _ => Task.FromResult(false), start: _ => { }),
            "config.lua",
            TimeSpan.FromMilliseconds(200));

        var panes = await driver.ListPanesAsync();

        Assert.Empty(panes);
    }

    [Fact]
    public void Name_caps_and_current_pane_pass_through_without_touching_the_launcher()
    {
        var inner = new FakeMuxDriver { CurrentPane = new PaneId("7") };

        var driver = new AutoStartDriver(
            inner,
            new WezTermInstanceLauncher(
                reachable: _ => throw new InvalidOperationException("should not be called"),
                start: _ => throw new InvalidOperationException("should not be called")),
            "config.lua",
            TimeSpan.FromSeconds(1));

        Assert.Equal(inner.Name, driver.Name);
        Assert.Equal(inner.Caps, driver.Caps);
        Assert.Equal(inner.CurrentPane, driver.CurrentPane);
    }
}
