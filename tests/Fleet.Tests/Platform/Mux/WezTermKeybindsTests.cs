using Fleet.Platform.Mux.WezTerm;
using Fleet.Platform.Mux.WezTerm.Models;
using Fleet.Shared.Keymap.Models;
using Fleet.Ui;

namespace Fleet.Tests.Platform.Mux;

public class WezTermKeybindsTests
{
    private const string Request = @"C:\fleet\workspace.request";

    private static string Lua(Keymap? keymap = null, string exe = "fleet") =>
        WezTermKeybinds.Generate(keymap ?? Keymap.Default, exe, Request);

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
        var lua = Lua(exe: "C:\bin\fleet.exe");

        Assert.Contains("key = ' '", lua);
        Assert.Contains("mods = 'CTRL'", lua);
    }

    [Fact]
    public void The_generated_lua_follows_a_rebound_prefix()
    {
        var lua = Lua(new Keymap(KeymapConfig.Default.WithPrefix("Ctrl+A")));

        Assert.Contains("key = 'a'", lua);
        Assert.DoesNotContain("key = ' '", lua);
    }

    [Fact]
    public void Windows_paths_are_escaped_for_lua()
    {
        var lua = Lua(exe: @"C:\bin\fleet.exe");

        Assert.Contains(@"C:\\bin\\fleet.exe", lua);
        Assert.Contains(@"C:\\fleet\\workspace.request", lua);
    }

    [Fact]
    public void The_prefix_opens_fleets_own_menu_in_a_tab()
    {
        var lua = Lua();

        Assert.Contains("act.SpawnCommandInNewTab {", lua);
        Assert.Contains("args = { M.fleet, 'menu', '--project', project },", lua);
    }

    [Fact]
    public void Wezterm_no_longer_draws_the_menu_itself()
    {
        var lua = Lua();

        Assert.DoesNotContain("InputSelector", lua);
        Assert.DoesNotContain("ActivateKeyTable", lua);
        Assert.DoesNotContain("M.menu", lua);
        Assert.DoesNotContain("M.dashboard_actions", lua);
    }

    [Fact]
    public void The_menu_is_scoped_to_windows_that_contain_a_fleet_pane()
    {
        var lua = Lua();

        Assert.Contains("local function fleet_project(window)", lua);
        Assert.Contains("get_user_vars()", lua);
        Assert.Contains("if not project then", lua);
    }

    [Fact]
    public void Without_a_fleet_pane_the_chord_is_forwarded_to_the_pane()
    {
        Assert.Contains("act.SendKey { key = ' ', mods = 'CTRL' }", Lua());
    }

    [Fact]
    public void The_module_exposes_apply_and_warns_it_is_generated()
    {
        var lua = Lua();

        Assert.Contains("function M.apply(config)", lua);
        Assert.Contains("return M", lua);
        Assert.Contains("overwritten", lua);
    }

    [Fact]
    public void The_module_also_exposes_setup_for_configs_written_against_the_predecessor()
    {
        Assert.Contains("function M.setup(config, _opts)", Lua());
    }

    [Fact]
    public void The_module_switches_workspace_because_the_cli_cannot()
    {
        var lua = Lua();

        Assert.Contains("wezterm.on('update-status'", lua);
        Assert.Contains("act.SwitchToWorkspace { name = wanted }", lua);
        Assert.Contains("os.remove(M.workspace_request)", lua);
    }

}
