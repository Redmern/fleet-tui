using Fleet.Platform.Mux.WezTerm;
using Fleet.Platform.Mux.WezTerm.Models;
using Fleet.Shared.Keymap.Models;
using Fleet.Ui;

namespace Fleet.Tests.Platform.Mux;

public class WezTermKeybindsTests
{
    [Theory]
    [InlineData("Ctrl+Space", " ", "CTRL")]
    [InlineData("Ctrl+A", "a", "CTRL")]
    [InlineData("Ctrl+Shift+P", "p", "CTRL|SHIFT")]
    [InlineData("Esc", "Escape", "NONE")]
    [InlineData("F5", "F5", "NONE")]
    public void A_key_becomes_a_wezterm_chord(string keyText, string key, string mods)
    {
        var chord = WezTermChord.From(keyText);

        Assert.Equal(key, chord.Key);
        Assert.Equal(mods, chord.Mods);
    }

    [Fact]
    public void The_generated_lua_binds_the_configured_prefix()
    {
        var lua = WezTermKeybinds.Generate(Keymap.Default, "C:\\bin\\fleet.exe");

        Assert.Contains("key = ' '", lua);
        Assert.Contains("mods = 'CTRL'", lua);
    }

    [Fact]
    public void The_generated_lua_follows_a_rebound_prefix()
    {
        var keymap = new Keymap(KeymapConfig.Default.WithPrefix("Ctrl+A"));

        var lua = WezTermKeybinds.Generate(keymap, "fleet");

        Assert.Contains("key = 'a'", lua);
        Assert.DoesNotContain("key = ' '", lua);
    }

    [Fact]
    public void Windows_paths_are_escaped_for_lua()
    {
        var lua = WezTermKeybinds.Generate(Keymap.Default, "C:\\bin\\fleet.exe");

        Assert.Contains("C:\\\\bin\\\\fleet.exe", lua);
    }

    [Fact]
    public void The_binding_runs_fleet_menu_in_a_split()
    {
        var lua = WezTermKeybinds.Generate(Keymap.Default, "fleet");

        Assert.Contains("'menu'", lua);
        Assert.Contains("SplitPane", lua);
        Assert.Contains("InputSelector", lua);
        Assert.Contains("'add-repository'", lua);
    }

    [Fact]
    public void The_module_exposes_apply_and_warns_it_is_generated()
    {
        var lua = WezTermKeybinds.Generate(Keymap.Default, "fleet");

        Assert.Contains("function M.apply(config)", lua);
        Assert.Contains("return M", lua);
        Assert.Contains("overwritten", lua);
    }

    [Fact]
    public void The_module_also_exposes_setup_for_configs_written_against_the_predecessor()
    {
        var lua = WezTermKeybinds.Generate(Keymap.Default, "fleet");

        Assert.Contains("function M.setup(config, _opts)", lua);
    }
}
