using Fleet.Platform.Mux;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Mux.Models;
using Fleet.Shared.Constants;

namespace Fleet.Tests.Platform.Mux;

public class NvimConfigDriverTests
{
    [Fact]
    public async Task An_nvim_pane_gets_fleets_app_name_when_the_setting_is_fleet()
    {
        var fake = new FakeMuxDriver();
        var mux = new NvimConfigDriver(fake, () => true);

        var id = await mux.SpawnAsync(new SpawnOptions { NewWindow = true, Args = AgentHarness.CommandFor(AgentHarness.Nvim) });

        Assert.Equal(AgentHarness.FleetNvimAppName, fake.EnvFor(id)[AgentHarness.NvimAppNameVariable]);
    }

    [Fact]
    public async Task A_titled_browse_pane_gets_it_too()
    {
        var fake = new FakeMuxDriver();
        var mux = new NvimConfigDriver(fake, () => true);

        var id = await mux.SpawnAsync(new SpawnOptions { NewWindow = true, Args = AgentHarness.BrowseCommandFor("files") });

        Assert.Equal(AgentHarness.FleetNvimAppName, fake.EnvFor(id)[AgentHarness.NvimAppNameVariable]);
    }

    [Fact]
    public async Task The_user_setting_leaves_the_env_alone()
    {
        var fake = new FakeMuxDriver();
        var mux = new NvimConfigDriver(fake, () => false);

        var id = await mux.SpawnAsync(new SpawnOptions { NewWindow = true, Args = AgentHarness.CommandFor(AgentHarness.Nvim) });

        Assert.False(fake.EnvFor(id).ContainsKey(AgentHarness.NvimAppNameVariable));
    }

    [Fact]
    public async Task A_claude_pane_keeps_its_own_env_and_never_asks_for_the_setting()
    {
        var fake = new FakeMuxDriver();
        var asked = false;
        var mux = new NvimConfigDriver(fake, () => asked = true);

        var id = await mux.SpawnAsync(new SpawnOptions
        {
            NewWindow = true,
            Args = AgentHarness.CommandFor(AgentHarness.Claude),
            Env = AgentHarness.SessionPersistence,
        });

        Assert.False(asked);
        Assert.Equal(AgentHarness.SessionPersistence, fake.EnvFor(id));
    }

    [Fact]
    public void An_app_name_already_in_the_env_wins()
    {
        var env = new Dictionary<string, string> { [AgentHarness.NvimAppNameVariable] = "mine" };

        var result = AgentHarness.WithFleetNvimConfig(AgentHarness.CommandFor(AgentHarness.Nvim), env);

        Assert.Equal("mine", result[AgentHarness.NvimAppNameVariable]);
    }
}
