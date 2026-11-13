using Fleet.Shared;

namespace Fleet.Tests.Shared;

public sealed class FleetHomeTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "fleet-home-" + Path.GetRandomFileName());
    private readonly IDisposable _scope;

    public FleetHomeTests() => _scope = FleetHome.Scope(_home);

    public void Dispose() => _scope.Dispose();

    [Fact]
    public async Task A_scope_opened_in_the_test_class_constructor_holds_in_the_test_method_across_awaits()
    {
        Assert.Equal(_home, FleetHome.Config);

        await Task.Yield();

        Assert.Equal(_home, FleetHome.Config);
        Assert.Equal(_home, await Task.Run(() => FleetHome.Config));
    }

    [Fact]
    public void An_inner_scope_wins_until_it_is_disposed()
    {
        var inner = Path.Combine(_home, "inner");

        using (FleetHome.Scope(inner))
        {
            Assert.Equal(inner, FleetHome.Config);
        }

        Assert.Equal(_home, FleetHome.Config);
    }

    [Fact]
    public async Task Parallel_flows_each_see_their_own_scope()
    {
        var gate = new Barrier(2);

        async Task<string> Run(string home)
        {
            using var scope = FleetHome.Scope(home);
            await Task.Yield();
            gate.SignalAndWait(TimeSpan.FromSeconds(5));
            return FleetHome.Config;
        }

        var seen = await Task.WhenAll(
            Task.Run(() => Run("/tmp/fleet-a")),
            Task.Run(() => Run("/tmp/fleet-b")));

        Assert.Equal(["/tmp/fleet-a", "/tmp/fleet-b"], seen);
    }

    [Fact]
    public void Without_a_scope_the_config_home_comes_from_the_environment_or_the_app_data_folder()
    {
        _scope.Dispose();

        var over = Environment.GetEnvironmentVariable(FleetHome.OverrideVariable);
        var expected = string.IsNullOrWhiteSpace(over)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "fleet")
            : over;
        Assert.Equal(expected, FleetHome.Config);
    }
}
