using Fleet.Platform.Mux.Embedded.Input;
using Fleet.Shared.Keybinds;
using Fleet.Shared.Keybinds.Enums;
using Fleet.Shared.Keymap;

namespace Fleet.Tests.Shared.Keybinds;

public class KeybindDefaultsTests
{
    private static KeybindSet Set => KeybindDefaults.Set;

    [Fact]
    public void The_embedded_defaults_load_without_problems()
    {
        Assert.NotEmpty(Set.Entries);
    }

    [Fact]
    public void Ids_are_unique()
    {
        var duplicates = Set.Entries.GroupBy(e => e.Id).Where(g => g.Count() > 1).Select(g => g.Key);

        Assert.Empty(duplicates);
    }

    [Theory]
    [InlineData("h", "left")]
    [InlineData("j", "down")]
    [InlineData("k", "up")]
    [InlineData("l", "right")]
    public void Alt_hjkl_resizes_and_Ctrl_hjkl_focuses_in_the_mux_and_nvim(string key, string direction)
    {
        var resize = Assert.Single(Set.Entries, e => e.Chord == $"Alt+{key}");
        var focus = Assert.Single(Set.Entries, e => e.Chord == $"Ctrl+{key}");

        Assert.Equal($"resize-{direction}", resize.Action);
        Assert.Equal($"focus-{direction}", focus.Action);
        Assert.All([resize, focus], e =>
        {
            Assert.Equal(["direct"], e.Targets[KeybindTarget.Mux]);
            Assert.Equal(["n", "t"], e.Targets[KeybindTarget.Nvim]);
        });
    }

    [Fact]
    public void Alt_n_leaves_the_claude_terminal_for_normal_mode_in_nvim()
    {
        var entry = Assert.Single(Set.For(KeybindTarget.Nvim, KeybindOs.Windows), b => b.Action == "claude-normal-mode");

        Assert.Equal("Alt+n", entry.Chord);
        Assert.Equal(["t"], entry.Contexts);
    }

    [Fact]
    public void Every_target_has_bindings_and_there_is_no_wezterm_target()
    {
        Assert.All(Enum.GetValues<KeybindTarget>(), t => Assert.NotEmpty(Set.For(t, KeybindOs.Linux)));
        Assert.DoesNotContain("wezterm", KeybindDefaults.Json(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Every_chord_is_already_normalised()
    {
        Assert.All(Set.Entries, e => Assert.Equal(KeybindChord.Normalize(e.Chord), e.Chord));
    }

    [Fact]
    public void The_fleet_ui_entries_match_the_fleet_ui_defaults()
    {
        Assert.Equal(KeymapDefaults.Prefix, Set.Find(KeybindLegacy.FleetUiPrefixId)!.Chord);

        foreach (var (action, key) in KeymapDefaults.Bindings)
        {
            var entry = Set.Find(KeybindLegacy.FleetUiId(action));

            Assert.True(entry is not null, $"no keybind for {action}");
            Assert.Equal(key, entry.Chord);
            Assert.True(entry.Targets.ContainsKey(KeybindTarget.FleetUi));
        }

        Assert.Equal(
            KeymapDefaults.Bindings.Count + 1,
            Set.For(KeybindTarget.FleetUi, KeybindOs.Windows).Count);
    }

    [Fact]
    public void The_mux_entries_match_the_mux_defaults_except_alt_hjkl_which_now_resize()
    {
        var mux = Set.For(KeybindTarget.Mux, KeybindOs.Windows);

        Assert.Equal(KeybindChord.Normalize(MuxKeys.DefaultPrefix), Set.Find(KeybindLegacy.MuxPrefixId)!.Chord);
        AssertContext(mux, "prefix", MuxKeys.DefaultPrefixKeys);
        AssertContext(
            mux,
            "direct",
            MuxKeys.DefaultDirectKeys
                .Where(k => !k.Key.StartsWith("alt+", StringComparison.Ordinal) || k.Key.Length != 5)
                .Concat(new Dictionary<string, string>
                {
                    ["alt+h"] = "resize left",
                    ["alt+j"] = "resize down",
                    ["alt+k"] = "resize up",
                    ["alt+l"] = "resize right",
                })
                .ToDictionary());
    }

    private static void AssertContext(
        IReadOnlyList<Fleet.Shared.Keybinds.Models.KeybindBinding> mux,
        string context,
        IReadOnlyDictionary<string, string> expected)
    {
        var actual = mux
            .Where(b => b.Contexts.Contains(context))
            .ToDictionary(b => b.Chord, b => b.Action);

        Assert.Equal(
            expected.ToDictionary(k => KeybindChord.Normalize(k.Key), k => KeybindActions.FromMux(k.Value)).OrderBy(k => k.Key, StringComparer.Ordinal),
            actual.OrderBy(k => k.Key, StringComparer.Ordinal));
    }
}
