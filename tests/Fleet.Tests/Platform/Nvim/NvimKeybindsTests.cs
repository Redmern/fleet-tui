using Fleet.Platform.Nvim;
using Fleet.Shared.Keybinds;
using Fleet.Shared.Keybinds.Enums;
using Fleet.Shared.Keybinds.Models;

namespace Fleet.Tests.Platform.Nvim;

public sealed class NvimKeybindsTests : IDisposable
{
    private readonly string _config = Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    public void Dispose()
    {
        if (Directory.Exists(_config))
        {
            Directory.Delete(_config, recursive: true);
        }
    }

    private static IReadOnlyList<KeybindBinding> Shipped => KeybindDefaults.Set.For(KeybindTarget.Nvim, KeybindOs.Windows);

    [Fact]
    public void The_shipped_nvim_keybinds_become_lua_rows()
    {
        var lua = NvimKeybinds.Generate(Shipped);

        Assert.StartsWith(NvimKeybinds.GeneratedHeader + "\n", lua);
        Assert.Contains("  { id = \"focus-left\", action = \"focus-left\", lhs = \"<C-h>\", modes = { \"n\", \"t\" } },\n", lua);
        Assert.Contains("  { id = \"resize-left\", action = \"resize-left\", lhs = \"<A-h>\", modes = { \"n\", \"t\" } },\n", lua);
        Assert.Contains("  { id = \"resize-right\", action = \"resize-right\", lhs = \"<A-l>\", modes = { \"n\", \"t\" } },\n", lua);
        Assert.Contains("  { id = \"claude-normal-mode\", action = \"claude-normal-mode\", lhs = \"<A-n>\", modes = { \"t\" } },\n", lua);
        Assert.EndsWith("return M\n", lua);
        Assert.DoesNotContain("\r", lua);
    }

    [Fact]
    public void Only_nvim_bindings_are_rendered()
    {
        var lua = NvimKeybinds.Generate(Shipped);

        Assert.DoesNotContain("newline", lua);
        Assert.DoesNotContain("fleet-ui.", lua);
        Assert.DoesNotContain("mux.", lua);
    }

    [Fact]
    public void Alt_n_leaves_terminal_mode_only_in_claude_terminal_buffers()
    {
        var lua = NvimKeybinds.Generate(Shipped);

        Assert.Contains("'<C-\\\\><C-n>', { buffer = buf", lua);
        Assert.Contains("vim.api.nvim_create_autocmd('TermOpen'", lua);
        Assert.Contains("if claude_terminal(ev.buf) then", lua);
    }

    [Fact]
    public void Resize_uses_smart_splits_when_it_is_there_and_plain_resize_otherwise()
    {
        var lua = NvimKeybinds.Generate(Shipped);

        Assert.Contains("pcall(require, 'smart-splits')", lua);
        Assert.Contains("'resize ' .. (grow and '+' or '-') .. M.resize_step", lua);
    }

    [Fact]
    public void An_unbound_keybind_is_left_out()
    {
        var set = KeybindDefaults.Set;
        set = set.With(set.Find("claude-normal-mode")! with { Chord = KeybindChord.Unbound });

        var lua = NvimKeybinds.Generate(set.For(KeybindTarget.Nvim, KeybindOs.Windows));

        Assert.DoesNotContain("id = \"claude-normal-mode\"", lua);
    }

    [Fact]
    public void An_os_chord_wins_on_that_os()
    {
        var set = KeybindDefaults.Set;
        set = set.With(set.Find("resize-left")! with
        {
            OsChords = new Dictionary<KeybindOs, string> { [KeybindOs.MacOs] = "Ctrl+Alt+h" },
        });

        Assert.Contains("lhs = \"<C-A-h>\"", NvimKeybinds.Generate(set.For(KeybindTarget.Nvim, KeybindOs.MacOs)));
        Assert.DoesNotContain("<C-A-h>", NvimKeybinds.Generate(set.For(KeybindTarget.Nvim, KeybindOs.Windows)));
    }

    [Fact]
    public void Quotes_in_an_id_cannot_break_the_lua()
    {
        var binding = new KeybindBinding("my \"odd\" \\ id", "resize-left", "Alt+h", ["n"]);

        Assert.Contains("id = \"my \\\"odd\\\" \\\\ id\"", NvimKeybinds.Generate([binding]));
    }

    [Fact]
    public void Control_characters_in_a_context_cannot_break_the_lua()
    {
        var binding = new KeybindBinding("id", "resize-left", "Alt+h", ["n\r\nx"]);

        Assert.Contains("modes = { \"n\\013\\010x\" }", NvimKeybinds.Generate([binding]));
    }

    [Fact]
    public void A_bad_mode_cannot_stop_the_other_keybinds() =>
        Assert.Contains("pcall(vim.keymap.set, modes, lhs, rhs, opts)", NvimKeybinds.Generate(Shipped));

    [Fact]
    public void Install_still_succeeds_when_the_generated_keybinds_cannot_be_written()
    {
        Directory.CreateDirectory(Path.Combine(_config, "lua", "fleet", NvimKeybinds.GeneratedFile));

        Assert.True(FleetNvimConfig.Install(_config));

        Assert.True(File.Exists(Path.Combine(_config, "init.lua")));
    }

    [Fact]
    public void The_user_module_has_the_same_bindings_under_its_own_header()
    {
        var generated = NvimKeybinds.Generate(Shipped);
        var user = NvimKeybinds.GenerateUserModule(Shipped);

        Assert.StartsWith(NvimKeybinds.UserModuleHeader + "\n", user);
        Assert.Equal(
            generated[(NvimKeybinds.GeneratedHeader.Length + 1)..],
            user[(NvimKeybinds.UserModuleHeader.Length + 1)..]);
    }

    [Fact]
    public void Writing_puts_the_generated_file_under_lua_fleet_and_is_idempotent()
    {
        var files = new NvimKeybindFiles(_config, KeybindOs.Windows);

        Assert.True(files.WriteGenerated(KeybindDefaults.Set));
        var written = File.GetLastWriteTimeUtc(files.GeneratedFile);
        Assert.True(files.WriteGenerated(KeybindDefaults.Set));

        Assert.Equal(Path.Combine(_config, "lua", "fleet", "keybinds.generated.lua"), files.GeneratedFile);
        Assert.Equal(NvimKeybinds.Generate(Shipped), File.ReadAllText(files.GeneratedFile));
        Assert.Equal(written, File.GetLastWriteTimeUtc(files.GeneratedFile));
    }

    [Fact]
    public void The_user_module_goes_where_it_is_asked_to()
    {
        var path = Path.Combine(_config, "elsewhere", NvimKeybinds.UserModuleFile);

        Assert.True(new NvimKeybindFiles(_config, KeybindOs.Windows).WriteUserModule(KeybindDefaults.Set, path));

        Assert.Equal(NvimKeybinds.GenerateUserModule(Shipped), File.ReadAllText(path));
        Assert.False(File.Exists(Path.Combine(_config, "lua", "fleet", NvimKeybinds.GeneratedFile)));
    }

    [Fact]
    public void Install_ships_the_generated_keybinds_and_keymaps_loads_them()
    {
        Assert.True(FleetNvimConfig.Install(_config));

        var generated = Path.Combine(_config, "lua", "fleet", NvimKeybinds.GeneratedFile);
        Assert.Equal(NvimKeybinds.Generate(KeybindDefaults.Set.For(KeybindTarget.Nvim)), File.ReadAllText(generated));
        Assert.Contains(
            "/lua/fleet/" + NvimKeybinds.GeneratedFile,
            File.ReadAllText(Path.Combine(_config, "lua", "fleet", "keymaps.lua")));
    }

    [Fact]
    public void Install_renders_the_keybinds_it_is_given()
    {
        var set = KeybindDefaults.Set;
        set = set.With(set.Find("resize-left")! with { Chord = "Alt+y", OsChords = new Dictionary<KeybindOs, string>() });

        Assert.True(FleetNvimConfig.Install(_config, set));

        Assert.Contains("lhs = \"<A-y>\"", File.ReadAllText(Path.Combine(_config, "lua", "fleet", NvimKeybinds.GeneratedFile)));
    }

    // Install rewrites only embedded files, so the generated keybinds must never be one of them.
    [Fact]
    public void The_generated_keybinds_are_not_embedded()
    {
        var names = typeof(FleetNvimConfig).Assembly.GetManifestResourceNames()
            .Select(n => n.Replace('\\', '/'))
            .ToList();

        Assert.DoesNotContain("nvim/lua/fleet/" + NvimKeybinds.GeneratedFile, names);
        Assert.DoesNotContain(names, n => n.EndsWith(NvimKeybinds.UserModuleFile, StringComparison.Ordinal));
    }
}
