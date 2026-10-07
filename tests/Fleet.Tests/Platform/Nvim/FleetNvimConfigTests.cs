using Fleet.Platform.Nvim;

namespace Fleet.Tests.Platform.Nvim;

public sealed class FleetNvimConfigTests : IDisposable
{
    private readonly string _target = Path.Combine(Path.GetTempPath(), "fleet-nvim-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_target))
        {
            Directory.Delete(_target, recursive: true);
        }
    }

    [Fact]
    public void Install_writes_the_shipped_config_from_the_binary()
    {
        Assert.True(FleetNvimConfig.Install(_target));

        Assert.Contains("lazy.nvim", File.ReadAllText(Path.Combine(_target, "init.lua")));
        Assert.Contains("claudecode.nvim", File.ReadAllText(Path.Combine(_target, "lua", "fleet", "plugins.lua")));
        Assert.Contains("neo-tree.nvim", File.ReadAllText(Path.Combine(_target, "lua", "fleet", "plugins.lua")));
    }

    [Fact]
    public void Install_puts_back_a_file_edited_by_hand()
    {
        FleetNvimConfig.Install(_target);
        var init = Path.Combine(_target, "init.lua");
        File.WriteAllText(init, "-- edited");

        Assert.True(FleetNvimConfig.Install(_target));

        Assert.Contains("lazy.nvim", File.ReadAllText(init));
    }

    [Fact]
    public void The_folder_is_named_after_fleets_app_name()
    {
        Assert.Equal("fleet-nvim", Path.GetFileName(FleetNvimConfig.Directory));
    }
}
