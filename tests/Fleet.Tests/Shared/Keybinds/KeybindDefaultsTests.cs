using Fleet.Features.Menu.ShowMenu;
using Fleet.Platform.Mux.Embedded.Input;
using Fleet.Shared.Keybinds;
using Fleet.Shared.Keybinds.Enums;
using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;

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
    public void The_mux_defaults_map_back_to_the_model_entries_they_came_from()
    {
        var mux = Set.For(KeybindTarget.Mux, KeybindOs.Windows);

        Assert.Equal(KeybindChord.Normalize(MuxKeys.DefaultPrefix), Set.Find(KeybindLegacy.MuxPrefixId)!.Chord);
        AssertContext(mux, "prefix", MuxKeys.DefaultPrefixKeys);
        AssertContext(mux, "direct", MuxKeys.DefaultDirectKeys);
    }

    // Fleet-menu leaf actions that deliberately have no ctrl+s binding. Submenu
    // openers are left out by MenuLeaves; add a row here, with a reason, only for
    // an action that must not be reachable from the mux prefix.
    private static readonly FleetAction[] ExemptFromCtrlS = [];

    private static IEnumerable<FleetAction> MenuLeaves =>
        new[] { FleetMenus.Main, FleetMenus.Settings, FleetMenus.FleetConfig }
            .SelectMany(FleetMenus.Actions)
            .Where(a => !FleetMenus.IsSubmenu(a))
            .Distinct();

    [Fact]
    public void Every_fleet_menu_action_opens_from_ctrl_s_or_is_explicitly_exempt()
    {
        var commands = MuxKeys.DefaultPrefixKeys.Values.ToHashSet(StringComparer.Ordinal);

        var missing = MenuLeaves
            .Except(ExemptFromCtrlS)
            .Where(a => !commands.Contains("menu " + FleetActionIds.For(a)))
            .ToList();

        Assert.True(missing.Count == 0, $"no ctrl+s binding for: {string.Join(", ", missing)}");
    }

    [Fact]
    public void The_exemption_list_holds_only_unbound_menu_actions()
    {
        var commands = MuxKeys.DefaultPrefixKeys.Values.ToHashSet(StringComparer.Ordinal);

        Assert.All(ExemptFromCtrlS, a =>
        {
            Assert.Contains(a, MenuLeaves);
            Assert.DoesNotContain("menu " + FleetActionIds.For(a), commands);
        });
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
