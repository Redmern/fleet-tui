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
    [InlineData("Ctrl+Enter", "Enter", "CTRL")]
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

        Assert.Contains("key = 'Enter'", lua);
        Assert.Contains("mods = 'CTRL'", lua);
    }

    [Fact]
    public void The_generated_lua_follows_a_rebound_prefix()
    {
        var lua = Lua(new Keymap(KeymapConfig.Default.WithPrefix("Ctrl+A")));

        Assert.Contains("key = 'a'", lua);
        Assert.DoesNotContain("key = 'Enter'", lua);
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
        Assert.Contains("local exe = M.fleet", lua);
        Assert.Contains("args = { exe, 'menu', '--project', project },", lua);
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

        Assert.Contains("local function fleet_project(window, quiet)", lua);
        Assert.Contains("get_user_vars()", lua);
        Assert.Contains("if not project then", lua);
    }

    [Fact]
    public void Without_a_fleet_pane_the_chord_is_forwarded_to_the_pane()
    {
        Assert.Contains("act.SendKey { key = 'Enter', mods = 'CTRL' }", Lua());
    }

    [Fact]
    public void The_module_exposes_the_project_lookup_instead_of_writing_the_status()
    {
        var lua = Lua();

        Assert.Contains("function M.project(window)", lua);
        Assert.Contains("function M.label(window)", lua);
        Assert.Contains("", lua);
        Assert.Contains("pcall(fleet_project, window, true)", lua);
    }

    [Fact]
    public void Fleet_never_writes_the_left_status_because_another_handler_owns_it()
    {
        var lua = Lua();

        Assert.DoesNotContain("window:set_left_status(", lua);

        var comment = lua.IndexOf("--   win:set_left_status", StringComparison.Ordinal);

        Assert.True(comment > 0, "the recipe for the host config should still be documented");
        Assert.DoesNotContain("win:set_left_status", lua[(comment + 30)..]);
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

    [Fact]
    public void The_module_refuses_the_leader_close_binding_on_the_dashboard_pane()
    {
        var lua = Lua();

        Assert.Contains("mods = 'LEADER'", lua);
        Assert.Contains("refusing to close the dashboard pane", lua);
        Assert.Contains("act.CloseCurrentPane { confirm = true }", lua);
    }

    [Fact]
    public void The_module_maps_published_tab_states_to_colored_markers()
    {
        var lua = WezTermKeybinds.Generate(
            Keymap.Default, "fleet", Request, tabStateGlob: @"C:\fleet\tabstate-*.txt");

        Assert.Contains("M.tabstate_glob = 'C:/fleet/tabstate-*.txt'", lua);
        Assert.Contains("function M.tab_marker(tab)", lua);
        Assert.Contains("working = '#f9e2af'", lua);
        Assert.Contains("waiting = '#f38ba8'", lua);
        Assert.Contains("idle = '#a6e3a1'", lua);
        Assert.Contains("utf8.char(0x25cf)", lua);
    }

    [Fact]
    public void An_empty_tab_state_glob_disables_the_marker_lookup()
    {
        var lua = Lua();

        Assert.Contains("M.tabstate_glob = ''", lua);
        Assert.Contains("if M.tabstate_glob == '' then", lua);
    }

    [Fact]
    public void User_vars_carry_the_signals_across_mux_and_ssh_domains()
    {
        var lua = Lua();

        Assert.Contains("wezterm.on('user-var-changed'", lua);
        Assert.Contains("'fleet-notify'", lua);
        Assert.Contains("'fleet-workspace'", lua);
        Assert.Contains("'fleet-tabstate'", lua);
        Assert.Contains("pushed_states[proj] = map", lua);
    }

    [Fact]
    public void The_file_polling_stays_as_a_local_fallback()
    {
        var lua = Lua();

        Assert.Contains("wezterm.on('update-status'", lua);
        Assert.Contains("os.remove(M.workspace_request)", lua);
    }

    [Fact]
    public void The_menu_chord_spawns_from_the_remote_path_on_other_domains()
    {
        var lua = Lua();

        Assert.Contains("pane:get_domain_name()", lua);
        Assert.Contains("domain ~= 'local'", lua);
        Assert.Contains("domain = 'CurrentPaneDomain',", lua);
    }
}
