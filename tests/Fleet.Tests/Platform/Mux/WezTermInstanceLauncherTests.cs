using Fleet.Platform.Mux.WezTerm;

namespace Fleet.Tests.Platform.Mux;

public class WezTermInstanceLauncherTests
{
    [Fact]
    public async Task Already_reachable_never_starts_a_process()
    {
        var started = false;

        var launcher = new WezTermInstanceLauncher(
            reachable: _ => Task.FromResult(true),
            start: _ => started = true);

        var ok = await launcher.EnsureRunningAsync("config.lua", TimeSpan.FromSeconds(1));

        Assert.True(ok);
        Assert.False(started);
    }

    [Fact]
    public async Task Unreachable_starts_once_and_polls_until_it_answers()
    {
        var starts = 0;
        var attempts = 0;

        var launcher = new WezTermInstanceLauncher(
            reachable: _ => Task.FromResult(++attempts > 2),
            start: _ => starts++);

        var ok = await launcher.EnsureRunningAsync("config.lua", TimeSpan.FromSeconds(5));

        Assert.True(ok);
        Assert.Equal(1, starts);
        Assert.True(attempts > 2);
    }

    [Fact]
    public async Task Gives_up_after_the_timeout_without_hanging()
    {
        var launcher = new WezTermInstanceLauncher(
            reachable: _ => Task.FromResult(false),
            start: _ => { });

        var ok = await launcher.EnsureRunningAsync(
            "config.lua", TimeSpan.FromMilliseconds(500));

        Assert.False(ok);
    }

    [Fact]
    public async Task Starts_with_the_configured_config_file()
    {
        string? started = null;

        var launcher = new WezTermInstanceLauncher(
            reachable: _ => Task.FromResult(started is not null),
            start: file => started = file);

        await launcher.EnsureRunningAsync(@"C:\fleet\wezterm\fleet-instance.lua", TimeSpan.FromSeconds(1));

        Assert.Equal(@"C:\fleet\wezterm\fleet-instance.lua", started);
    }
}
