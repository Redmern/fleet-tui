using Fleet.Platform.Mux.WezTerm;

namespace Fleet.Tests.Platform.Mux;

public class WezTermInstanceTests
{
    [Fact]
    public void The_socket_and_config_live_under_the_given_root()
    {
        Assert.Equal(
            Path.Combine("/fleet/wezterm", "fleet.sock"),
            WezTermInstance.SocketPath("/fleet/wezterm"));

        Assert.Equal(
            Path.Combine("/fleet/wezterm", "fleet-instance.lua"),
            WezTermInstance.ConfigFile("/fleet/wezterm"));
    }

    [Fact]
    public void The_generated_config_declares_an_isolated_domain()
    {
        var lua = WezTermInstance.Config(@"C:\fleet\wezterm\fleet.sock", @"C:\fleet\module");

        Assert.Contains("config.unix_domains = {", lua);
        Assert.Contains("name = 'fleet',", lua);
        Assert.Contains(@"socket_path = 'C:\\fleet\\wezterm\\fleet.sock',", lua);
        Assert.Contains("config.default_domain = 'fleet'", lua);
        Assert.Contains("config.default_gui_startup_args = { 'connect', 'fleet' }", lua);
    }

    [Fact]
    public void The_generated_config_loads_the_existing_keybind_module_by_path()
    {
        var lua = WezTermInstance.Config(@"C:\fleet\wezterm\fleet.sock", @"C:\fleet\module");

        Assert.Contains(@"package.path = package.path .. ';C:\\fleet\\module\\?.lua'", lua);
        Assert.Contains("pcall(require, 'fleet-theme')", lua);
        Assert.Contains("pcall(require, 'fleet')", lua);
        Assert.Contains("fleet.apply(config)", lua);
        Assert.EndsWith("return config\n", lua);
    }
}
