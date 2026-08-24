using Fleet.Platform.Mux.WezTerm;

namespace Fleet.Tests.Platform.Mux;

public class WezTermWiringTests
{
    [Fact]
    public void The_module_goes_where_that_platform_keeps_its_wezterm_lua()
    {
        var directory = WezTermWiring.ModuleDirectory("/home/x");

        Assert.Equal(
            OperatingSystem.IsWindows()
                ? Path.Combine("/home/x", ".wezterm")
                : Path.Combine("/home/x", ".config", "wezterm"),
            directory);
    }

    [Fact]
    public void Both_wezterm_config_locations_are_considered()
    {
        var candidates = WezTermWiring.ConfigCandidates("/home/x");

        Assert.Equal(Path.Combine("/home/x", ".wezterm.lua"), candidates[0]);
        Assert.Equal(Path.Combine("/home/x", ".config", "wezterm", "wezterm.lua"), candidates[1]);
    }

    [Theory]
    [InlineData("local fleet = require 'fleet'")]
    [InlineData("local ok, fleet = pcall(require, \"fleet\")")]
    [InlineData("local ok, f = pcall(require, 'fleet')")]
    [InlineData("  local fleet = require(\"fleet\")")]
    public void Any_shape_of_require_counts_as_wired(string line)
    {
        Assert.True(WezTermWiring.AlreadyWired($"local config = {{}}\n{line}\nreturn config"));
    }

    [Fact]
    public void A_commented_out_require_does_not_count()
    {
        Assert.False(WezTermWiring.AlreadyWired("-- local fleet = require 'fleet'\nreturn config"));
    }

    [Fact]
    public void A_config_without_fleet_is_not_wired()
    {
        Assert.False(WezTermWiring.AlreadyWired("local wezterm = require 'wezterm'\nreturn config"));
    }

    [Fact]
    public void Wiring_goes_in_before_the_final_return_or_it_would_never_run()
    {
        var text = "local config = wezterm.config_builder()\nconfig.font_size = 11\nreturn config\n";

        var wired = WezTermWiring.Wire(text);

        Assert.True(wired.BeforeReturn);
        Assert.True(WezTermWiring.AlreadyWired(wired.Text));

        var lines = WezTermWiring.Lines(wired.Text).Where(l => l.Trim().Length > 0).ToList();

        Assert.Equal("return config", lines[^1].Trim());

        var applied = lines.FindIndex(l => l.Contains(WezTermWiring.ApplyLine, StringComparison.Ordinal));

        Assert.True(applied > 0 && applied < lines.Count - 1, "the block has to run before the return");
    }

    [Fact]
    public void A_config_with_no_return_gets_the_block_appended_and_says_so()
    {
        var wired = WezTermWiring.Wire("config.font_size = 11\n");

        Assert.False(wired.BeforeReturn);
        Assert.True(WezTermWiring.AlreadyWired(wired.Text));
    }

    [Fact]
    public void The_last_return_config_is_the_one_that_matters()
    {
        var text = string.Join(
            '\n',
            "if x then",
            "  return config",
            "end",
            "return config");

        var wired = WezTermWiring.Wire(text);
        var lines = WezTermWiring.Lines(wired.Text).Where(l => l.Trim().Length > 0).ToList();

        Assert.Equal("return config", lines[^1].Trim());
        Assert.Equal(2, lines.Count(l => l.Trim() == "return config"));
    }

    [Fact]
    public void The_starter_config_loads_fleet_and_returns_the_config()
    {
        var starter = WezTermWiring.Starter();

        Assert.Contains("local wezterm = require 'wezterm'", starter);
        Assert.Contains("wezterm.config_builder()", starter);
        Assert.Contains("pcall(require, 'fleet')", starter);
        Assert.Contains(WezTermWiring.ApplyLine, starter);
        Assert.EndsWith("return config\n", starter);
        Assert.True(WezTermWiring.AlreadyWired(starter));
    }

    [Fact]
    public void The_default_config_sits_beside_the_module_off_windows()
    {
        var chosen = WezTermWiring.DefaultConfig("/home/u");

        if (OperatingSystem.IsWindows())
        {
            Assert.EndsWith(".wezterm.lua", chosen);
        }
        else
        {
            Assert.Equal(WezTermWiring.ModuleDirectory("/home/u"),
                Path.GetDirectoryName(chosen));
        }
    }
}
