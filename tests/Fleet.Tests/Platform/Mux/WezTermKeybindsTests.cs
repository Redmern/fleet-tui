using Fleet.Platform.Mux.WezTerm;
using Fleet.Platform.Mux.WezTerm.Models;
using Fleet.Shared.Keymap.Models;
using Fleet.Ui;

namespace Fleet.Tests.Platform.Mux;

public class WezTermKeybindsTests
{
    private const string Request = @"C:\fleet\workspace.request";

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
        var lua = WezTermKeybinds.Generate(Keymap.Default, "C:\\bin\\fleet.exe", Request);

        Assert.Contains("key = ' '", lua);
        Assert.Contains("mods = 'CTRL'", lua);
    }

    [Fact]
    public void The_generated_lua_follows_a_rebound_prefix()
    {
        var keymap = new Keymap(KeymapConfig.Default.WithPrefix("Ctrl+A"));

        var lua = WezTermKeybinds.Generate(keymap, "fleet", Request);

        Assert.Contains("key = 'a'", lua);
        Assert.DoesNotContain("key = ' '", lua);
    }

    [Fact]
    public void Windows_paths_are_escaped_for_lua()
    {
        var lua = WezTermKeybinds.Generate(Keymap.Default, "C:\\bin\\fleet.exe", Request);

        Assert.Contains("C:\\\\bin\\\\fleet.exe", lua);
    }

    [Fact]
    public void The_menu_is_a_key_table_rather_than_fuzzy_matching()
    {
        var lua = WezTermKeybinds.Generate(Keymap.Default, "fleet", Request);

        Assert.Contains("act.ActivateKeyTable { name = 'fleet', one_shot = true }", lua);
        Assert.Contains("config.key_tables['fleet'] = entries", lua);
        Assert.DoesNotContain("InputSelector", lua);
        Assert.DoesNotContain("fuzzy =", lua);
    }

    [Fact]
    public void Every_menu_entry_carries_a_key()
    {
        var lua = WezTermKeybinds.Generate(Keymap.Default, "fleet", Request);

        Assert.Contains("M.menu = {", lua);
        Assert.Contains("id = 'add-repository'", lua);
        Assert.Contains("id = 'new-agent'", lua);
    }

    [Fact]
    public void Escape_leaves_the_menu_without_running_anything()
    {
        var lua = WezTermKeybinds.Generate(Keymap.Default, "fleet", Request);

        Assert.Contains("key = 'Escape', action = act.PopKeyTable", lua);
    }

    [Fact]
    public void The_entries_are_listed_while_the_table_is_active()
    {
        var lua = WezTermKeybinds.Generate(Keymap.Default, "fleet", Request);

        Assert.Contains("window:active_key_table() == 'fleet'", lua);
        Assert.Contains("window:set_left_status", lua);
    }

    [Fact]
    public void Actions_the_dashboard_renders_are_handed_to_it_instead_of_opening_a_pane()
    {
        var lua = WezTermKeybinds.Generate(Keymap.Default, "fleet", Request);

        Assert.Contains("M.dashboard_actions = {", lua);
        Assert.Contains("['add-repository'] = true", lua);
        Assert.Contains("if project and M.dashboard_actions[id] then", lua);
        Assert.Contains("wezterm.background_child_process {", lua);
        Assert.Contains("'request', '--action', id, '--project', project", lua);
    }

    [Fact]
    public void Actions_the_dashboard_cannot_render_still_open_their_own_pane()
    {
        var lua = WezTermKeybinds.Generate(Keymap.Default, "fleet", Request);

        Assert.Contains("SplitPane", lua);
        Assert.Contains("'menu'", lua);
        Assert.DoesNotContain("['open-project'] = true", lua);
    }

    [Fact]
    public void The_menu_is_scoped_to_windows_that_contain_a_fleet_pane()
    {
        var lua = WezTermKeybinds.Generate(Keymap.Default, "fleet", Request);

        Assert.Contains("local function fleet_project(window)", lua);
        Assert.Contains("get_user_vars()", lua);
        Assert.Contains("if not fleet_project(window) then", lua);
    }

    [Fact]
    public void Without_a_fleet_pane_the_chord_is_forwarded_to_the_pane()
    {
        var lua = WezTermKeybinds.Generate(Keymap.Default, "fleet", Request);

        Assert.Contains("act.SendKey { key = ' ', mods = 'CTRL' }", lua);
    }

    [Fact]
    public void The_module_exposes_apply_and_warns_it_is_generated()
    {
        var lua = WezTermKeybinds.Generate(Keymap.Default, "fleet", Request);

        Assert.Contains("function M.apply(config)", lua);
        Assert.Contains("return M", lua);
        Assert.Contains("overwritten", lua);
    }

    [Fact]
    public void The_module_also_exposes_setup_for_configs_written_against_the_predecessor()
    {
        var lua = WezTermKeybinds.Generate(Keymap.Default, "fleet", Request);

        Assert.Contains("function M.setup(config, _opts)", lua);
    }

    [Fact]
    public void The_module_switches_workspace_because_the_cli_cannot()
    {
        var lua = WezTermKeybinds.Generate(Keymap.Default, "fleet", Request);

        Assert.Contains("wezterm.on('update-status'", lua);
        Assert.Contains("act.SwitchToWorkspace { name = wanted }", lua);
        Assert.Contains("os.remove(M.workspace_request)", lua);
    }

    [Fact]
    public void The_workspace_request_path_is_escaped_for_lua()
    {
        var lua = WezTermKeybinds.Generate(Keymap.Default, "fleet", Request);

        Assert.Contains(@"C:\\fleet\\workspace.request", lua);
    }

    [Fact]
    public void Hiding_an_agent_is_offered_in_the_menu()
    {
        var lua = WezTermKeybinds.Generate(Keymap.Default, "fleet", Request);

        Assert.Contains("'toggle-hidden'", lua);
        Assert.Contains("['toggle-hidden'] = true", lua);
    }
}
