using System.Text;
using Fleet.Platform.Mux.Embedded.Input;
using Fleet.Platform.Mux.Embedded.Native;

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
        Assert.Equal("tab 3", keys.PrefixCommand(Key.Digit3, Mods.None, "3"));
        Assert.Equal("menu", keys.DirectCommand(Key.Enter, Mods.Ctrl, null));
        Assert.Equal("smart-focus left", keys.DirectCommand(Key.H, Mods.Ctrl, null));
        Assert.Equal("prev-tab", keys.DirectCommand(Key.ArrowLeft, Mods.Alt, null));
        Assert.Null(keys.DirectCommand(Key.H, Mods.None, "h"));
    }

    [Fact]
    public void Unix_bytes_find_the_longest_binding()
    {
        var keys = MuxKeys.Defaults;

        Assert.Equal(("resize left", 3), keys.PrefixBytes("\e[D"u8));
        Assert.Equal(("smart-focus left", 2), keys.DirectBytes("\eh"u8));
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
    public void Which_key_groups_directions_arrows_and_tab_numbers()
    {
        var entries = WhichKey.For(MuxKeys.Defaults);

        Assert.Contains(entries, e => e.Key == "h j k l" && e.Label == "focus");
        Assert.Contains(entries, e => e.Key == "← → ↑ ↓" && e.Label == "resize");
        Assert.Contains(entries, e => e.Key == "1-9" && e.Label == "go to tab");
        Assert.Contains(entries, e => e.Key == "%" && e.Label == "split right");
        Assert.Contains(entries, e => e.Key == "ctrl+s" && e.Label == "send ctrl+s");
        Assert.DoesNotContain(entries, e => e.Label == "tab 1");
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
        var groups = entries.TakeWhile(e => e.Group).ToList();
        var singles = entries.Skip(groups.Count).ToList();

        Assert.Equal(["focus", "go to tab", "resize"], groups.Select(g => g.Label).Order());
        Assert.All(singles, e => Assert.False(e.Group));
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
        var prefix = new Prefix(KeyChord.Parse("ctrl+s")!.Value);

        Assert.Equal(PrefixCommand.None, prefix.OnKey(Key.S, Mods.None, "s"));
        Assert.Equal(PrefixCommand.Armed, prefix.OnKey(Key.S, Mods.Ctrl));
        Assert.Equal(PrefixCommand.Chord, prefix.OnKey(Key.H, Mods.None, "h"));
        Assert.Equal(PrefixCommand.Armed, prefix.OnKey(Key.S, Mods.Ctrl));
        Assert.Equal(PrefixCommand.SendPrefix, prefix.OnKey(Key.S, Mods.Ctrl));
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
