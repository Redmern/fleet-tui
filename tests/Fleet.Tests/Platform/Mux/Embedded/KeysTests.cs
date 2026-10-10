using System.Text;
using Fleet.Features.Menu.ShowMenu;
using Fleet.Platform.Mux.Embedded.Input;
using Fleet.Platform.Mux.Embedded.Native;
using Fleet.Shared.Keybinds;
using Fleet.Shared.Keybinds.Enums;
using Fleet.Shared.Keybinds.Models;
using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;

namespace Fleet.Tests.Platform.Mux.Embedded;

public class KeysTests
{
    [Theory]
    [InlineData("ctrl+s", "\u0013")]
    [InlineData("ctrl+h", "\b")]
    [InlineData("alt+h", "\eh")]
    [InlineData("alt+left", "\e[1;3D")]
    [InlineData("left", "\e[D")]
    [InlineData("shift+tab", "\e[Z")]
    [InlineData("%", "%")]
    [InlineData("\"", "\"")]
    [InlineData("space", " ")]
    [InlineData("pageup", "\e[5~")]
    [InlineData("enter", "\r")]
    [InlineData("ctrl+enter", "\e[27;5;13~")]
    [InlineData("shift+enter", "\e[27;2;13~")]
    public void A_chord_knows_the_bytes_a_unix_terminal_sends_for_it(string spec, string bytes)
    {
        Assert.Equal(Encoding.ASCII.GetBytes(bytes), KeyChord.Parse(spec)!.Value.Bytes());
    }

    [Theory]
    [InlineData("ctrl+tab")]
    public void Chords_a_unix_terminal_cannot_tell_apart_have_no_bytes(string spec)
    {
        Assert.Null(KeyChord.Parse(spec)!.Value.Bytes());
    }

    [Fact]
    public void Ctrl_enter_and_shift_enter_reach_their_bindings_in_either_report_form()
    {
        var keys = MuxKeys.Defaults;

        Assert.Equal("menu", keys.DirectBytes(Encoding.ASCII.GetBytes("\e[27;5;13~")).Command);
        Assert.Equal("newline", keys.DirectBytes(Encoding.ASCII.GetBytes("\e[27;2;13~")).Command);
        Assert.Equal("\e[27;5;13~", Encoding.ASCII.GetString(ModifiedKeys.Normalize(Encoding.ASCII.GetBytes("\e[13;5u"))));
        Assert.Equal("abc", Encoding.ASCII.GetString(ModifiedKeys.Normalize(Encoding.ASCII.GetBytes("abc"))));
    }

    [Theory]
    [InlineData("\e[27;5;13~", "\r")]
    [InlineData("\e[27;5;9~", "\t")]
    [InlineData("\e[27;2;9~", "\e[Z")]
    [InlineData("\e[27;6;65~", "\u0001")]
    [InlineData("\e[27;3;120~", "\ex")]
    [InlineData("\e[27;2;33~", "!")]
    public void A_modified_key_no_binding_wants_goes_to_the_pane_as_its_usual_bytes(string report, string legacy)
    {
        var (modifier, code, length) = ModifiedKeys.Parse(Encoding.ASCII.GetBytes(report))!.Value;

        Assert.Equal(report.Length, length);
        Assert.Equal(legacy, Encoding.UTF8.GetString(ModifiedKeys.Legacy(modifier, code)));
    }

    [Fact]
    public void Text_that_only_starts_like_a_report_is_left_alone()
    {
        Assert.Null(ModifiedKeys.Parse(Encoding.ASCII.GetBytes("\e[27;5")));
        Assert.Null(ModifiedKeys.Parse(Encoding.ASCII.GetBytes("\e[5~")));
    }

    [Fact]
    public void A_character_chord_matches_by_text_and_a_named_chord_by_key_and_modifiers()
    {
        var percent = KeyChord.Parse("%")!.Value;
        var ctrlH = KeyChord.Parse("ctrl+h")!.Value;

        Assert.True(percent.Matches(Key.Digit5, Mods.Shift, "%"));
        Assert.False(percent.Matches(Key.Digit5, Mods.Ctrl | Mods.Shift, "%"));
        Assert.True(ctrlH.Matches(Key.H, Mods.Ctrl | Mods.NumLock, null));
        Assert.False(ctrlH.Matches(Key.H, Mods.Ctrl | Mods.Shift, null));
        Assert.False(ctrlH.Matches(Key.H, Mods.None, "h"));
    }

    [Fact]
    public void An_unknown_key_or_modifier_is_reported()
    {
        Assert.Throws<FormatException>(() => KeyChord.Parse("hyper+x"));
        Assert.Throws<FormatException>(() => KeyChord.Parse("ctrl+banana"));
    }

    [Fact]
    public void The_defaults_follow_the_wezterm_tmux_layout()
    {
        var keys = MuxKeys.Defaults;

        Assert.Equal("ctrl+s", keys.Prefix.Label);
        Assert.Equal("focus-left", keys.PrefixCommand(Key.H, Mods.None, "h"));
        Assert.Equal("split-right", keys.PrefixCommand(Key.Digit5, Mods.Shift, "%"));
        Assert.Equal("split-down", keys.PrefixCommand(Key.Quote, Mods.Shift, "\""));
        Assert.Equal("resize left", keys.PrefixCommand(Key.ArrowLeft, Mods.None, null));
        Assert.Equal("menu", keys.DirectCommand(Key.Enter, Mods.Ctrl, null));
        Assert.Equal("smart-focus left", keys.DirectCommand(Key.H, Mods.Ctrl, null));
        Assert.Equal("prev-tab", keys.DirectCommand(Key.ArrowLeft, Mods.Alt, null));
        Assert.Null(keys.DirectCommand(Key.H, Mods.None, "h"));
    }

    [Fact]
    public void The_defaults_come_from_the_keybind_model_and_only_alt_hjkl_changed_to_resize()
    {
        var prefix = new Dictionary<string, string>
        {
            ["h"] = "focus-left",
            ["j"] = "focus-down",
            ["k"] = "focus-up",
            ["l"] = "focus-right",
            ["left"] = "resize left",
            ["right"] = "resize right",
            ["up"] = "resize up",
            ["down"] = "resize down",
            ["%"] = "split-right",
            ["\""] = "split-down",
            ["x"] = "kill-pane",
            ["z"] = "zoom",
            ["o"] = "next-pane",
            ["space"] = "menu",
            ["["] = "copy-mode",
            ["]"] = "paste",
            ["d"] = "detach",
            ["Q"] = "menu quit",
            ["f f"] = "float-new",
            ["f t"] = "float-toggle",
            ["f e"] = "float-embed",
            ["f g"] = "float-mode",
            ["w w"] = "next-workspace",
            ["w s"] = "switch-project",
            ["t c"] = "new-tab",
            ["t n"] = "next-tab",
            ["t p"] = "prev-tab",
            ["t 1"] = "tab 1",
            ["t 2"] = "tab 2",
            ["t 3"] = "tab 3",
            ["t 4"] = "tab 4",
            ["t 5"] = "tab 5",
            ["t 6"] = "tab 6",
            ["t 7"] = "tab 7",
            ["t 8"] = "tab 8",
            ["t 9"] = "tab 9",
            ["t x"] = "kill-tab",
            ["s p"] = "menu switch-project",
            ["s w"] = "menu save-session",
            ["s R"] = "menu remotes",
            ["s o"] = "menu open-port",
            ["s d"] = "detach",
            ["s q"] = "detach",
            ["s r"] = "reload",
            ["a m"] = "menu main-pane",
            ["a l"] = "menu list-agents",
            ["a e"] = "menu open-editor",
            ["a f"] = "menu browsefiles",
            ["a n"] = "menu notifications",
            ["g k"] = "menu keybinds",
            ["g K"] = "menu show-menu-keys",
            ["g t"] = "menu button-hints",
            ["g T"] = "menu theme",
            ["g c v"] = "menu main-orchestrator-nvim",
            ["g c V"] = "menu sub-orchestrators-nvim",
            ["g c N"] = "menu nvim-config",
            ["g c i"] = "menu auto-close",
            ["g c A"] = "menu aidlc-mode",
            ["g c C"] = "menu claude-profile",
            ["g c e"] = "menu edit-fleet-config",
            ["g c H"] = "menu head-model",
            ["g c M"] = "menu main-model",
            ["g c S"] = "menu sub-model",
            ["g c R"] = "menu agent-model",
            ["g c p"] = "menu settings",
            ["m b"] = "menu rebuild-dashboard",
            ["m x"] = "menu cleanup",
            ["m X"] = "menu hide-all",
            ["m l"] = "menu viewlogs",
            ["m u"] = "menu update",
            ["m v"] = "menu version",
            ["m W"] = "menu whats-new",
        };
        var direct = new Dictionary<string, string>
        {
            ["ctrl+h"] = "smart-focus left",
            ["ctrl+j"] = "smart-focus down",
            ["ctrl+k"] = "smart-focus up",
            ["ctrl+l"] = "smart-focus right",
            ["alt+h"] = "smart-resize left",
            ["alt+j"] = "smart-resize down",
            ["alt+k"] = "smart-resize up",
            ["alt+l"] = "smart-resize right",
            ["alt+left"] = "prev-tab",
            ["alt+right"] = "next-tab",
            ["ctrl+tab"] = "next-tab",
            ["ctrl+shift+tab"] = "prev-tab",
            ["ctrl+enter"] = "menu",
            ["shift+enter"] = "newline",
        };

        Assert.Equal("ctrl+s", MuxKeys.DefaultPrefix);
        Assert.Equal(prefix.OrderBy(k => k.Key, StringComparer.Ordinal), MuxKeys.DefaultPrefixKeys.OrderBy(k => k.Key, StringComparer.Ordinal));
        Assert.Equal(direct.OrderBy(k => k.Key, StringComparer.Ordinal), MuxKeys.DefaultDirectKeys.OrderBy(k => k.Key, StringComparer.Ordinal));
    }

    [Fact]
    public void A_model_rebind_moves_the_mux_default_and_none_unbinds_it()
    {
        var set = KeybindLayering.Apply(
            KeybindDefaults.Set,
            new Dictionary<string, KeybindEntryJson>
            {
                ["resize-left"] = new() { Chord = "Alt+y" },
                ["focus-down"] = new() { Chord = "none" },
                ["mux.zoom"] = new() { Chord = "Z" },
                ["mux.kill-pane"] = new() { Chord = "F20" },
            });

        var direct = MuxKeybinds.Keys(set, KeybindOs.Linux, KeybindLegacy.MuxDirect);
        var prefixed = MuxKeybinds.Keys(set, KeybindOs.Linux, KeybindLegacy.MuxPrefixed);

        Assert.Equal("smart-resize left", direct["alt+y"]);
        Assert.False(direct.ContainsKey("alt+h"));
        Assert.False(direct.ContainsKey("ctrl+j"));
        Assert.Equal("zoom", prefixed["Z"]);
        Assert.False(prefixed.ContainsKey("z"));
        Assert.DoesNotContain("kill-pane", prefixed.Values);
    }

    [Theory]
    [InlineData("Ctrl+Shift+Tab", "ctrl+shift+tab")]
    [InlineData("Space", "space")]
    [InlineData("Alt+Left", "alt+left")]
    [InlineData("Ctrl++", "ctrl++")]
    [InlineData("Alt+H", "alt+h")]
    [InlineData("Ctrl+Shift+G", "ctrl+shift+g")]
    [InlineData("G", "G")]
    [InlineData("f f", "f f")]
    public void A_model_chord_becomes_the_spec_embedded_keys_json_uses(string chord, string spec)
    {
        Assert.Equal(spec, MuxKeybinds.Spec(chord));
    }

    [Fact]
    public void A_keys_file_still_overrides_the_model_defaults_for_alt_hjkl()
    {
        var keys = MuxKeys.From(new MuxKeysFile { Keys = new() { ["alt+h"] = "smart-focus left", ["alt+j"] = "none" } }, null);

        Assert.Equal("smart-focus left", keys.DirectCommand(Key.H, Mods.Alt, null));
        Assert.Null(keys.DirectCommand(Key.J, Mods.Alt, null));
        Assert.Equal("smart-resize up", keys.DirectCommand(Key.K, Mods.Alt, null));
    }

    [Fact]
    public void Unix_bytes_find_the_longest_binding()
    {
        var keys = MuxKeys.Defaults;

        Assert.Equal(("resize left", 3), keys.PrefixBytes("\e[D"u8));
        Assert.Equal(("smart-focus left", 1), keys.DirectBytes("\b"u8));
        Assert.Equal(("smart-resize left", 2), keys.DirectBytes("\eh"u8));
        Assert.Equal(("smart-resize down", 2), keys.DirectBytes("\ej"u8));
        Assert.Equal(("smart-resize up", 2), keys.DirectBytes("\ek"u8));
        Assert.Equal(("smart-resize right", 2), keys.DirectBytes("\el"u8));
        Assert.Equal(("prev-tab", 6), keys.DirectBytes("\e[1;3D"u8));
        Assert.Equal((null, 0), keys.DirectBytes("x"u8));
    }

    [Fact]
    public void A_keys_file_overrides_rebinds_and_unbinds_over_the_defaults()
    {
        var keys = MuxKeys.From(
            new MuxKeysFile
            {
                Prefix = "ctrl+b",
                PrefixKeys = new() { ["x"] = "none", ["v"] = "split-right" },
                Keys = new() { ["ctrl+h"] = "none" },
            },
            null);

        Assert.Equal("ctrl+b", keys.Prefix.Label);
        Assert.Null(keys.PrefixCommand(Key.X, Mods.None, "x"));
        Assert.Equal("split-right", keys.PrefixCommand(Key.V, Mods.None, "v"));
        Assert.Equal("split-right", keys.PrefixCommand(Key.Digit5, Mods.Shift, "%"));
        Assert.Null(keys.DirectCommand(Key.H, Mods.Ctrl, null));
        Assert.Equal("smart-focus down", keys.DirectCommand(Key.J, Mods.Ctrl, null));
    }

    [Fact]
    public void The_environment_prefix_wins_over_the_file()
    {
        var keys = MuxKeys.From(new MuxKeysFile { Prefix = "ctrl+b" }, "ctrl+a");

        Assert.Equal("ctrl+a", keys.Prefix.Label);
    }

    [Fact]
    public void A_broken_keys_file_falls_back_to_the_defaults_and_says_why()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fleet-keys-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ \"prefix\": \"ctrl+banana\" }");
        var lines = new List<string>();

        try
        {
            var keys = MuxKeys.Load(path, null, lines.Add);

            Assert.Equal("ctrl+s", keys.Prefix.Label);
            Assert.Contains(lines, l => l.Contains("banana", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_keys_file_may_have_comments_and_trailing_commas()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fleet-keys-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{\n  // my prefix\n  \"prefix\": \"ctrl+b\",\n}");

        try
        {
            Assert.Equal("ctrl+b", MuxKeys.Load(path, null, _ => { }).Prefix.Label);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Which_key_folds_directions_and_arrows_at_the_root_and_tab_numbers_in_the_tabs_group()
    {
        var keys = MuxKeys.Defaults;
        var entries = WhichKey.For(keys);
        var tabs = WhichKey.For(Group(keys, "t"), keys.Prefix);

        Assert.Contains(entries, e => e.Key == "h j k l" && e.Label == "focus" && e.Fold && !e.Group);
        Assert.Contains(entries, e => e.Key == "← → ↑ ↓" && e.Label == "resize" && e.Fold && !e.Group);
        Assert.Contains(entries, e => e.Key == "%" && e.Label == "split right");
        Assert.Contains(entries, e => e.Key == "ctrl+s" && e.Label == "send ctrl+s");
        Assert.DoesNotContain(entries, e => e.Label == "go to tab");
        Assert.Equal(
            [("1-9", "go to tab"), ("c", "new tab"), ("n", "next tab"), ("p", "prev tab"), ("x", "close tab")],
            tabs.Select(e => (e.Key, e.Label)));
        Assert.True(tabs[0].Fold);
    }

    [Fact]
    public void Default_groups_and_folds_carry_their_nerd_font_icons()
    {
        var keys = MuxKeys.Defaults;
        var entries = WhichKey.For(keys);
        var configure = WhichKey.For(Group(keys, "g"), keys.Prefix);
        var tabs = WhichKey.For(Group(keys, "t"), keys.Prefix);

        Assert.Equal("", entries.Single(e => e.Label == "float").Icon);
        Assert.Equal("", entries.Single(e => e.Label == "project").Icon);
        Assert.Equal("", entries.Single(e => e.Label == "session").Icon);
        Assert.Equal("", entries.Single(e => e.Label == "agents").Icon);
        Assert.Equal("", entries.Single(e => e.Label == "tabs").Icon);
        Assert.Equal("", entries.Single(e => e.Label == "configure").Icon);
        Assert.Equal("", entries.Single(e => e.Label == "maintenance").Icon);
        Assert.Equal("", configure.Single(e => e.Label == "fleet config").Icon);
        Assert.Equal("", entries.Single(e => e.Label == "focus").Icon);
        Assert.Equal("", entries.Single(e => e.Label == "resize").Icon);
        Assert.Equal("", tabs.Single(e => e.Label == "go to tab").Icon);
        Assert.All(entries.Concat(configure).Where(e => !(e.Group || e.Fold)), e => Assert.Null(e.Icon));
        Assert.All(
            entries.Concat(configure).Concat(tabs).Where(e => e.Icon is not null),
            e => Assert.InRange(e.Icon![0], '', ''));
    }

    [Fact]
    public void Show_icons_false_clears_every_icon()
    {
        var keys = MuxKeys.From(new MuxKeysFile { ShowIcons = false, Icons = new() { ["f"] = "" } }, null);

        Assert.False(keys.ShowIcons);
        Assert.All(WhichKey.For(keys), e => Assert.Null(e.Icon));
    }

    [Fact]
    public void The_icons_map_overrides_removes_and_adds_icons()
    {
        var lines = new List<string>();
        var keys = MuxKeys.From(
            new MuxKeysFile
            {
                PrefixKeys = new() { ["y s"] = "split-down" },
                Icons = new() { ["f"] = "", ["s"] = "none", ["y"] = "", ["w"] = "\U0001F4C1" },
            },
            null,
            log: lines.Add);
        var entries = WhichKey.For(keys);

        Assert.Equal("", entries.Single(e => e.Label == "float").Icon);
        Assert.Null(entries.Single(e => e.Label == "session").Icon);
        Assert.Equal("", entries.Single(e => e.Key == "y").Icon);
        Assert.Equal("", entries.Single(e => e.Label == "project").Icon);
        Assert.Contains(lines, l => l.Contains("icon for \"w\" ignored", StringComparison.Ordinal));
    }

    [Fact]
    public void Which_key_marks_only_real_submenus_as_groups()
    {
        var keys = MuxKeys.Defaults;
        var root = WhichKey.For(keys);

        Assert.Equal(
            [
                ("a", "agents"),
                ("f", "float"),
                ("g", "configure"),
                ("m", "maintenance"),
                ("s", "session"),
                ("t", "tabs"),
                ("w", "project"),
            ],
            root.Where(e => e.Group).Select(e => (e.Key, e.Label)).Order());

        var floats = WhichKey.For(Group(keys, "f"), keys.Prefix);

        Assert.Equal(["e", "f", "g", "t"], floats.Select(e => e.Key));
        Assert.All(floats, e => Assert.False(e.Group || e.Fold));
        Assert.DoesNotContain(floats, e => e.Label.StartsWith("send", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("1 2 3 4 5 6 7 8 9", "1-9")]
    [InlineData("1 2 3", "1-3")]
    [InlineData("1 2", "1 2")]
    [InlineData("1 2 4", "1 2 4")]
    [InlineData("3 2 1", "3 2 1")]
    [InlineData("h j k l", "h j k l")]
    public void Which_key_compresses_only_a_consecutive_run_of_digits(string labels, string expected)
    {
        Assert.Equal(expected, WhichKey.Keys(labels.Split(' ')));
    }

    [Fact]
    public void Which_key_lists_groups_first_then_single_keys_alphanumerically()
    {
        var entries = WhichKey.For(MuxKeys.Defaults);
        var groups = entries.TakeWhile(e => e.Group || e.Fold).ToList();
        var singles = entries.Skip(groups.Count).ToList();

        Assert.Equal(["agents", "configure", "float", "focus", "maintenance", "project", "resize", "session", "tabs"], groups.Select(g => g.Label).Order());
        Assert.All(singles, e => Assert.False(e.Group || e.Fold));
        Assert.Equal(
            singles.Select(e => e.Key).OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ThenBy(k => k, StringComparer.Ordinal),
            singles.Select(e => e.Key));
        Assert.Equal(
            groups.Select(e => e.Key).OrderBy(k => k, StringComparer.OrdinalIgnoreCase),
            groups.Select(e => e.Key));
    }

    [Fact]
    public void The_prefix_arms_then_takes_the_next_key_and_a_second_prefix_sends_itself()
    {
        var root = MuxKeys.Defaults.Root;
        var prefix = new Prefix(KeyChord.Parse("ctrl+s")!.Value);

        Assert.Equal(PrefixCommand.None, prefix.OnKey(root, Key.S, Mods.None, "s"));
        Assert.Equal(PrefixCommand.Armed, prefix.OnKey(root, Key.S, Mods.Ctrl));
        Assert.Equal(PrefixCommand.Chord, prefix.OnKey(root, Key.H, Mods.None, "h"));
        Assert.Equal("focus-left", prefix.Command);
        Assert.Equal(PrefixCommand.Armed, prefix.OnKey(root, Key.S, Mods.Ctrl));
        Assert.Equal(PrefixCommand.SendPrefix, prefix.OnKey(root, Key.S, Mods.Ctrl));
        Assert.False(prefix.Armed);
    }

    [Fact]
    public void A_group_key_descends_and_the_next_key_runs_from_that_group()
    {
        var root = MuxKeys.Defaults.Root;
        var prefix = new Prefix(KeyChord.Parse("ctrl+s")!.Value);

        prefix.OnKey(root, Key.S, Mods.Ctrl);
        Assert.Equal(PrefixCommand.Descend, prefix.OnKey(root, Key.F, Mods.None, "f"));
        Assert.Equal("float", prefix.Node!.Label);
        Assert.Equal("ctrl+s › float", prefix.Breadcrumb);
        Assert.Equal(PrefixCommand.Chord, prefix.OnKey(root, Key.T, Mods.None, "t"));
        Assert.Equal("float-toggle", prefix.Command);
        Assert.False(prefix.Armed);
    }

    [Fact]
    public void Backspace_goes_up_a_level_and_closes_at_the_root()
    {
        var root = MuxKeys.Defaults.Root;
        var prefix = new Prefix(KeyChord.Parse("ctrl+s")!.Value);

        prefix.OnKey(root, Key.S, Mods.Ctrl);
        prefix.OnKey(root, Key.S, Mods.None, "s");
        Assert.Equal(PrefixCommand.Back, prefix.OnKey(root, Key.Backspace, Mods.None));
        Assert.Same(root, prefix.Node);
        Assert.Equal(PrefixCommand.Cancel, prefix.OnKey(root, Key.Backspace, Mods.None));
        Assert.False(prefix.Armed);
    }

    [Fact]
    public void Esc_or_an_unknown_key_closes_the_popup_at_any_depth()
    {
        var root = MuxKeys.Defaults.Root;
        var prefix = new Prefix(KeyChord.Parse("ctrl+s")!.Value);

        prefix.OnKey(root, Key.S, Mods.Ctrl);
        prefix.OnKey(root, Key.F, Mods.None, "f");
        Assert.Equal(PrefixCommand.Cancel, prefix.OnKey(root, Key.Escape, Mods.None));
        Assert.False(prefix.Armed);

        prefix.OnKey(root, Key.S, Mods.Ctrl);
        prefix.OnKey(root, Key.F, Mods.None, "f");
        Assert.Equal(PrefixCommand.Cancel, prefix.OnKey(root, Key.Z, Mods.None, "z"));
        Assert.False(prefix.Armed);
    }

    [Fact]
    public void The_prefix_inside_a_group_goes_back_to_the_root_instead_of_sending_itself()
    {
        var root = MuxKeys.Defaults.Root;
        var prefix = new Prefix(KeyChord.Parse("ctrl+s")!.Value);

        prefix.OnKey(root, Key.S, Mods.Ctrl);
        prefix.OnKey(root, Key.W, Mods.None, "w");
        Assert.Equal(PrefixCommand.Armed, prefix.OnKey(root, Key.S, Mods.Ctrl));
        Assert.Same(root, prefix.Node);
        Assert.Equal(PrefixCommand.SendPrefix, prefix.OnKey(root, Key.S, Mods.Ctrl));
    }

    [Theory]
    [InlineData("\u0013ft")]
    [InlineData("\u0013f|t")]
    [InlineData("\u0013|f|t")]
    public void Bytes_run_a_sequence_whether_it_comes_in_one_read_or_several(string reads)
    {
        var commands = new List<string>();
        var prefix = new Prefix(KeyChord.Parse("ctrl+s")!.Value);
        var root = MuxKeys.Defaults.Root;

        foreach (var read in reads.Split('|'))
        {
            commands.AddRange(Drive(prefix, root, Encoding.ASCII.GetBytes(read)));
        }

        Assert.Equal(["float-toggle"], commands);
        Assert.False(prefix.Armed);
    }

    [Fact]
    public void Bytes_go_back_with_delete_and_send_the_prefix_from_the_root()
    {
        var prefix = new Prefix(KeyChord.Parse("ctrl+s")!.Value);
        var root = MuxKeys.Defaults.Root;

        prefix.Arm(root);
        Assert.Equal(PrefixCommand.Descend, prefix.OnBytes(root, "s"u8, out _));
        Assert.Equal(PrefixCommand.Back, prefix.OnBytes(root, "\x7f"u8, out var back));
        Assert.Equal(1, back);
        Assert.Equal(PrefixCommand.SendPrefix, prefix.OnBytes(root, "\u0013"u8, out var sent));
        Assert.Equal(1, sent);
        Assert.False(prefix.Armed);
    }

    private static List<string> Drive(Prefix prefix, KeyNode root, byte[] bytes)
    {
        var commands = new List<string>();
        var i = 0;

        while (i < bytes.Length)
        {
            if (!prefix.Armed)
            {
                Assert.True(bytes.AsSpan(i).StartsWith(prefix.Bytes));
                prefix.Arm(root);
                i += prefix.Bytes!.Length;
                continue;
            }

            if (prefix.OnBytes(root, bytes.AsSpan(i), out var length) == PrefixCommand.Chord)
            {
                commands.Add(prefix.Command!);
            }

            i += length;
        }

        return commands;
    }

    private static KeyNode Group(MuxKeys keys, string spec, KeyNode? from = null) =>
        (from ?? keys.Root).Groups.Single(g => g.Spec == spec).Node;

    [Fact]
    public void A_sequence_in_the_keys_file_builds_a_group_with_its_label()
    {
        var keys = MuxKeys.From(
            new MuxKeysFile
            {
                PrefixKeys = new() { ["y s"] = "split-down", ["y v"] = "split-right" },
                Groups = new() { ["y"] = "git-ish stuff" },
            },
            null);

        var group = Group(keys, "y");
        Assert.Equal("git-ish stuff", group.Label);
        Assert.Equal(["split-down", "split-right"], group.Leaves.Select(b => b.Command).Order());
        Assert.Equal(new KeyStep.Enter(group), keys.Root.Match(Key.Y, Mods.None, "y"));
    }

    [Fact]
    public void A_group_without_a_label_is_labelled_by_its_key()
    {
        var keys = MuxKeys.From(new MuxKeysFile { PrefixKeys = new() { ["v a"] = "zoom" } }, null);

        Assert.Equal("v", Group(keys, "v").Label);
    }

    [Fact]
    public void Unbinding_a_group_drops_it_and_its_children()
    {
        var keys = MuxKeys.From(new MuxKeysFile { Groups = new() { ["s"] = "none" } }, null);

        Assert.DoesNotContain(keys.Root.Groups, g => g.Spec == "s");
        Assert.DoesNotContain(keys.PrefixKeys, b => b.Command == "reload");
        Assert.Equal("detach", keys.PrefixCommand(Key.D, Mods.None, "d"));
    }

    [Fact]
    public void A_group_whose_children_are_all_unbound_disappears()
    {
        var keys = MuxKeys.From(new MuxKeysFile { PrefixKeys = new() { ["w w"] = "none", ["w s"] = "none" } }, null);

        Assert.DoesNotContain(keys.Root.Groups, g => g.Spec == "w");
    }

    [Fact]
    public void A_user_leaf_wins_over_a_default_group_so_the_old_flat_layout_still_works()
    {
        var keys = MuxKeys.From(new MuxKeysFile { PrefixKeys = new() { ["f"] = "float-new" } }, null);

        Assert.Equal("float-new", keys.PrefixCommand(Key.F, Mods.None, "f"));
        Assert.DoesNotContain(keys.Root.Groups, g => g.Spec == "f");
        Assert.DoesNotContain(keys.PrefixKeys, b => b.Command == "float-toggle");
    }

    [Fact]
    public void A_user_sequence_wins_over_a_default_leaf()
    {
        var keys = MuxKeys.From(new MuxKeysFile { PrefixKeys = new() { ["z x"] = "kill-pane" } }, null);

        Assert.Null(keys.PrefixCommand(Key.Z, Mods.None, "z"));
        Assert.Equal("kill-pane", Group(keys, "z").Leaves.Single().Command);
    }

    [Fact]
    public void A_user_leaf_wins_over_a_user_sequence_on_the_same_key_and_says_so()
    {
        var lines = new List<string>();
        var keys = MuxKeys.From(
            new MuxKeysFile { PrefixKeys = new() { ["y"] = "float-toggle", ["y x"] = "zoom" } }, null, log: lines.Add);

        Assert.Equal("float-toggle", keys.PrefixCommand(Key.Y, Mods.None, "y"));
        Assert.DoesNotContain(keys.Root.Groups, g => g.Spec == "y");
        Assert.Contains("keys: \"y x\" ignored, \"y\" is already bound", lines);
    }

    [Fact]
    public void A_sequence_among_the_direct_keys_is_dropped_and_logged()
    {
        var lines = new List<string>();
        var keys = MuxKeys.From(new MuxKeysFile { Keys = new() { ["ctrl+g x"] = "zoom" } }, null, log: lines.Add);

        Assert.DoesNotContain(keys.DirectKeys, b => b.Command == "zoom");
        Assert.Single(lines);
    }

    [Fact]
    public void The_default_tree_has_one_binding_per_key_on_every_level()
    {
        var keys = MuxKeys.Defaults;

        void Check(KeyNode node)
        {
            var chords = node.Leaves.Select(b => b.Chord).Concat(node.Groups.Select(g => g.Chord)).ToList();
            Assert.Equal(chords.Count, chords.Distinct().Count());
            Assert.DoesNotContain(keys.Prefix, chords);
            foreach (var group in node.Groups)
            {
                Check(group.Node);
            }
        }

        Check(keys.Root);
        Assert.Equal(MuxKeys.DefaultPrefixKeys.Count, keys.PrefixKeys.Count);
        Assert.Equal(
            MuxKeys.DefaultGroups.Keys.Where(k => !k.Contains(' ')).Order(),
            keys.Root.Groups.Select(g => g.Spec).Order());
        Assert.Equal(["c"], Group(keys, "g").Groups.Select(g => g.Spec));
    }

    [Fact]
    public void The_agents_group_opens_each_menu_action_with_a_readable_label()
    {
        var keys = MuxKeys.Defaults;
        var agents = WhichKey.For(Group(keys, "a"), keys.Prefix);

        Assert.Equal(
            [
                ("e", "open editor here"),
                ("f", "file navigator"),
                ("l", "list agents"),
                ("m", "go to dashboard"),
                ("n", "notifications"),
            ],
            agents.Select(e => (e.Key, e.Label)));
        Assert.Equal(
            ["menu browsefiles", "menu list-agents", "menu main-pane", "menu notifications", "menu open-editor"],
            Group(keys, "a").Leaves.Select(b => b.Command).Order());
    }

    [Theory]
    [InlineData("menu", "fleet menu")]
    [InlineData("menu main-pane", "go to dashboard")]
    [InlineData("menu browsefiles", "file navigator")]
    [InlineData("menu no-such-action", "menu no such action")]
    public void Which_key_labels_a_menu_action_by_its_description(string command, string label)
    {
        Assert.Equal(label, WhichKey.Label(command));
    }

    [Fact]
    public void Embedded_keys_can_rebind_and_unbind_a_menu_action()
    {
        var keys = MuxKeys.From(
            new MuxKeysFile { PrefixKeys = new() { ["a m"] = "none", ["a d"] = "menu main-pane" } },
            null);

        Assert.Equal(
            ["d", "e", "f", "l", "n"],
            Group(keys, "a").Leaves.Select(b => b.Chord.Label).Order());
        Assert.Equal("menu main-pane", Group(keys, "a").Leaves.Single(b => b.Chord.Label == "d").Command);
    }

    [Fact]
    public void The_default_groups_hold_the_moved_keys()
    {
        var keys = MuxKeys.Defaults;

        Assert.Equal(
            ["float-embed", "float-mode", "float-new", "float-toggle"],
            Group(keys, "f").Leaves.Select(b => b.Command).Order());
        Assert.Equal(["next-workspace", "switch-project"], Group(keys, "w").Leaves.Select(b => b.Command).Order());
        Assert.Equal(
            ["detach", "detach", "menu open-port", "menu remotes", "menu save-session", "menu switch-project", "reload"],
            Group(keys, "s").Leaves.Select(b => b.Command).Order());
        Assert.Equal(
            ["kill-tab", "new-tab", "next-tab", "prev-tab", "tab 1", "tab 2", "tab 3", "tab 4", "tab 5", "tab 6", "tab 7", "tab 8", "tab 9"],
            Group(keys, "t").Leaves.Select(b => b.Command).Order());
        Assert.Null(keys.PrefixCommand(Key.R, Mods.None, "r"));
        Assert.Equal("detach", keys.PrefixCommand(Key.D, Mods.None, "d"));
        Assert.Equal("kill-pane", keys.PrefixCommand(Key.X, Mods.None, "x"));
    }

    [Theory]
    [InlineData("s")]
    [InlineData("q")]
    [InlineData("c")]
    [InlineData("n")]
    [InlineData("p")]
    [InlineData("&")]
    [InlineData("1")]
    [InlineData("9")]
    public void The_old_top_level_session_and_tab_keys_no_longer_run_anything(string key)
    {
        var keys = MuxKeys.Defaults;

        Assert.DoesNotContain(keys.Root.Leaves, b => b.Spec == key);
        Assert.DoesNotContain(MuxKeys.DefaultPrefixKeys.Keys, k => k.StartsWith("q ", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("tc", "new-tab")]
    [InlineData("tn", "next-tab")]
    [InlineData("tp", "prev-tab")]
    [InlineData("t3", "tab 3")]
    [InlineData("tx", "kill-tab")]
    [InlineData("sp", "menu switch-project")]
    [InlineData("sw", "menu save-session")]
    [InlineData("sR", "menu remotes")]
    [InlineData("so", "menu open-port")]
    [InlineData("sd", "detach")]
    [InlineData("sq", "detach")]
    [InlineData("sr", "reload")]
    [InlineData("gk", "menu keybinds")]
    [InlineData("gcH", "menu head-model")]
    [InlineData("gcp", "menu settings")]
    [InlineData("mW", "menu whats-new")]
    [InlineData("ml", "menu viewlogs")]
    [InlineData("Q", "menu quit")]
    public void A_group_key_then_an_action_key_runs_the_action(string typed, string command)
    {
        var prefix = new Prefix(KeyChord.Parse("ctrl+s")!.Value);
        var root = MuxKeys.Defaults.Root;

        Assert.Equal([command], Drive(prefix, root, Encoding.ASCII.GetBytes("\u0013" + typed)));
        Assert.False(prefix.Armed);
    }

    [Fact]
    public void Capital_q_quits_fleet_from_the_top_level_and_lower_q_is_free()
    {
        var keys = MuxKeys.Defaults;

        Assert.Equal("menu quit", keys.PrefixCommand(Key.Q, Mods.Shift, "Q"));
        Assert.Null(keys.PrefixCommand(Key.Q, Mods.None, "q"));
        Assert.DoesNotContain(keys.Root.Groups, g => g.Spec == "q");
        Assert.Contains(WhichKey.For(keys), e => e.Key == "Q" && e.Label == "quit fleet");
    }

    [Fact]
    public void The_fleet_config_group_nests_under_configure_with_its_own_breadcrumb()
    {
        var root = MuxKeys.Defaults.Root;
        var prefix = new Prefix(KeyChord.Parse("ctrl+s")!.Value);

        prefix.Arm(root);
        Assert.Equal(PrefixCommand.Descend, prefix.OnBytes(root, "g"u8, out _));
        Assert.Equal(PrefixCommand.Descend, prefix.OnBytes(root, "c"u8, out _));
        Assert.Equal("ctrl+s › configure › fleet config", prefix.Breadcrumb);
    }

    [Theory]
    [InlineData("s", "session")]
    [InlineData("g", "configure")]
    [InlineData("m", "maintenance")]
    public void Each_settings_section_has_a_group_holding_all_its_leaf_actions(string spec, string section)
    {
        var keys = MuxKeys.Defaults;
        var group = Group(keys, spec);
        var commands = group.Leaves.Select(b => b.Command).ToList();
        var actions = FleetMenus.Settings.Single(s => s.Header == section).Actions.Where(a => !FleetMenus.IsSubmenu(a));

        Assert.Equal(section, group.Label);
        Assert.All(actions, a => Assert.Contains("menu " + FleetActionIds.For(a), commands));
    }

    [Fact]
    public void The_fleet_config_group_holds_exactly_the_fleet_config_menu_actions()
    {
        var keys = MuxKeys.Defaults;
        var fleetConfig = Group(keys, "c", Group(keys, "g"));
        var expected = FleetMenus.Actions(FleetMenus.FleetConfig).Select(a => "menu " + FleetActionIds.For(a)).Order();

        Assert.Equal("fleet config", fleetConfig.Label);
        Assert.Equal(expected, fleetConfig.Leaves.Select(b => b.Command).Order());
    }

    [Fact]
    public void Every_default_menu_command_names_a_real_action_and_has_a_short_readable_label()
    {
        var menus = MuxKeys.DefaultPrefixKeys.Values.Where(c => c.StartsWith("menu ", StringComparison.Ordinal)).ToList();

        Assert.NotEmpty(menus);
        Assert.All(menus, c =>
        {
            Assert.NotEqual(FleetAction.None, FleetActionIds.Parse(c["menu ".Length..]));
            var label = WhichKey.Label(c);
            Assert.False(label.StartsWith("menu ", StringComparison.Ordinal), c);
            Assert.InRange(label.Length, 1, 30);
        });
    }

    [Fact]
    public void Embedded_keys_can_rebind_unbind_and_relabel_the_new_groups()
    {
        var keys = MuxKeys.From(
            new MuxKeysFile
            {
                PrefixKeys = new() { ["m v"] = "none", ["m V"] = "menu version", ["Q"] = "none", ["s Q"] = "menu quit" },
                Groups = new() { ["g c"] = "fleet" },
            },
            null);

        Assert.Equal("menu version", Group(keys, "m").Leaves.Single(b => b.Chord.Label == "V").Command);
        Assert.DoesNotContain(Group(keys, "m").Leaves, b => b.Chord.Label == "v");
        Assert.Null(keys.PrefixCommand(Key.Q, Mods.Shift, "Q"));
        Assert.Equal("menu quit", Group(keys, "s").Leaves.Single(b => b.Chord.Label == "Q").Command);
        Assert.Equal("fleet", Group(keys, "c", Group(keys, "g")).Label);
    }

    [Fact]
    public void Closing_asks_first_and_only_y_confirms()
    {
        var confirm = new ConfirmMode();

        confirm.Ask("kill-pane", "close this pane?");
        Assert.True(confirm.Active);
        Assert.Equal("close this pane? y/n", confirm.Badge);
        Assert.Equal("kill-pane", confirm.OnKey(Key.Y, Mods.None, "y")!.Name);
        Assert.False(confirm.Active);

        confirm.Ask("kill-tab", "close this tab?");
        Assert.Equal(1, confirm.OnBytes("n"u8, out var no));
        Assert.Null(no);
        Assert.False(confirm.Active);
    }

    private static readonly Dictionary<string, string> HeadKeys = new()
    {
        ["alt+o"] = "head",
        ["alt+shift+o"] = "head voice",
    };

    [Fact]
    public void Extra_direct_keys_bind_the_head_chords_on_both_paths()
    {
        var keys = MuxKeys.From(null, null, HeadKeys);

        Assert.Equal("head", keys.DirectCommand(Key.O, Mods.Alt, "o"));
        Assert.Equal("head voice", keys.DirectCommand(Key.O, Mods.Alt | Mods.Shift, "O"));
        Assert.Equal(("head", 2), keys.DirectBytes("\eo"u8));
        Assert.Equal(("head voice", 2), keys.DirectBytes("\eO"u8));
        Assert.Equal("menu", keys.DirectCommand(Key.Enter, Mods.Ctrl, null));
    }

    [Fact]
    public void The_keys_file_still_wins_over_extra_direct_keys()
    {
        var keys = MuxKeys.From(new MuxKeysFile { Keys = new() { ["alt+o"] = "none" } }, null, HeadKeys);

        Assert.Null(keys.DirectCommand(Key.O, Mods.Alt, "o"));
        Assert.Equal("head voice", keys.DirectCommand(Key.O, Mods.Alt | Mods.Shift, "O"));
    }

    [Fact]
    public void An_extra_key_that_does_not_parse_is_dropped_and_the_rest_still_load()
    {
        var keys = MuxKeys.From(null, null, new Dictionary<string, string> { ["hyper+o"] = "head" });

        Assert.Equal("menu", keys.DirectCommand(Key.Enter, Mods.Ctrl, null));
        Assert.DoesNotContain(keys.DirectKeys, b => b.Command == "head");
    }
}
