using Fleet.Shared.Keybinds;
using Fleet.Shared.Keybinds.Enums;
using Fleet.Shared.Keybinds.Models;

namespace Fleet.Tests.Shared.Keybinds;

public sealed class NvimChordTests
{
    [Theory]
    [InlineData("Alt+h", "<A-h>")]
    [InlineData("alt+H", "<A-H>")]
    [InlineData("Ctrl+l", "<C-l>")]
    [InlineData("Shift+Enter", "<S-CR>")]
    [InlineData("Ctrl+Shift+Tab", "<C-S-Tab>")]
    [InlineData("Cmd+s", "<D-s>")]
    [InlineData("Space", "<Space>")]
    [InlineData("Escape", "<Esc>")]
    [InlineData("F5", "<F5>")]
    [InlineData("q", "q")]
    [InlineData("<", "<lt>")]
    [InlineData("Ctrl+\\", "<C-Bslash>")]
    [InlineData("Ctrl++", "<C-+>")]
    [InlineData("+", "+")]
    [InlineData("f f", "ff")]
    [InlineData("Ctrl+w Left", "<C-w><Left>")]
    public void Translates_a_portable_chord_to_an_nvim_lhs(string chord, string lhs) =>
        Assert.Equal(lhs, NvimChord.Lhs(chord));

    [Fact]
    public void The_focus_table_follows_the_shipped_ctrl_hjkl()
    {
        Assert.Equal(
            "{{'<C-h>','h','Left',{'n','t'}},{'<C-j>','j','Down',{'n','t'}},"
            + "{'<C-k>','k','Up',{'n','t'}},{'<C-l>','l','Right',{'n','t'}}}",
            NvimFocusMaps.LuaTable(KeybindDefaults.Set));
    }

    [Fact]
    public void The_focus_table_follows_a_rebound_or_unbound_chord()
    {
        var set = KeybindDefaults.Set;
        set = set.With(set.Find("focus-left")! with { Chord = "Alt+'" });
        set = set.With(set.Find("focus-down")! with
        {
            Chord = KeybindChord.Unbound,
            OsChords = new Dictionary<KeybindOs, string>(),
        });

        var table = NvimFocusMaps.LuaTable(set);

        Assert.StartsWith("{{'<A-\\'>','h','Left',{'n','t'}},{'<C-k>'", table);
        Assert.DoesNotContain("Down", table);
    }

    [Fact]
    public void The_focus_table_leaves_out_bindings_for_other_targets()
    {
        var set = KeybindSet.Empty.With(new KeybindEntry(
            "mux-only",
            "focus-left",
            "Ctrl+h",
            new Dictionary<KeybindTarget, IReadOnlyList<string>> { [KeybindTarget.Mux] = ["direct"] },
            new Dictionary<KeybindOs, string>()));

        Assert.Equal("{}", NvimFocusMaps.LuaTable(set));
    }
}
