using Fleet.Shared.Keybinds;
using Fleet.Shared.Keybinds.Enums;
using Fleet.Shared.Keymap.Enums;

namespace Fleet.Tests.Shared.Keybinds;

public class KeybindLegacyTests
{
    private static KeybindSet Defaults => KeybindDefaults.Set;

    [Fact]
    public void Fleet_ui_bindings_and_prefix_rebind_their_entries()
    {
        var set = KeybindLegacy.FleetUi(
            Defaults,
            "Ctrl+B",
            new Dictionary<string, string> { ["Refresh"] = "F5", ["close"] = "x", ["NotARealAction"] = "z", ["Close "] = "" });

        Assert.Equal("Ctrl+B", set.Find(KeybindLegacy.FleetUiPrefixId)!.Chord);
        Assert.Equal("F5", set.Find(KeybindLegacy.FleetUiId(FleetAction.Refresh))!.Chord);
        Assert.Equal("x", set.Find(KeybindLegacy.FleetUiId(FleetAction.Close))!.Chord);
        Assert.Equal(Defaults.Entries.Count, set.Entries.Count);
    }

    [Fact]
    public void An_empty_fleet_ui_file_changes_nothing()
    {
        var set = KeybindLegacy.FleetUi(Defaults, string.Empty, new Dictionary<string, string>());

        Assert.Equal(Defaults.Entries, set.Entries);
    }

    [Fact]
    public void A_mux_key_set_to_its_default_command_changes_nothing()
    {
        var set = KeybindLegacy.Mux(Defaults, null, null, new Dictionary<string, string> { ["ctrl+h"] = "smart-focus left" });

        Assert.Equal(Defaults.Entries, set.Entries);
    }

    [Fact]
    public void Unbinding_a_shared_chord_in_the_mux_keeps_it_in_nvim()
    {
        var set = KeybindLegacy.Mux(Defaults, null, null, new Dictionary<string, string> { ["ctrl+h"] = "none" });

        Assert.DoesNotContain(set.For(KeybindTarget.Mux, KeybindOs.Windows), b => b.Chord == "Ctrl+h");
        Assert.Contains(set.For(KeybindTarget.Nvim, KeybindOs.Windows), b => b.Chord == "Ctrl+h" && b.Action == "focus-left");
    }

    [Fact]
    public void Remapping_a_shared_chord_in_the_mux_splits_it_off_for_the_mux_only()
    {
        var set = KeybindLegacy.Mux(Defaults, null, null, new Dictionary<string, string> { ["alt+h"] = "smart-focus left" });

        var mux = Assert.Single(set.For(KeybindTarget.Mux, KeybindOs.Windows), b => b.Chord == "Alt+h");
        Assert.Equal("focus-left", mux.Action);
        Assert.Contains(set.For(KeybindTarget.Nvim, KeybindOs.Windows), b => b.Chord == "Alt+h" && b.Action == "resize-left");
    }

    [Fact]
    public void Remapping_a_mux_only_chord_changes_it_in_place()
    {
        var set = KeybindLegacy.Mux(Defaults, null, null, new Dictionary<string, string> { ["alt+left"] = "next-tab" });

        Assert.Equal("next-tab", set.Find("mux.prev-tab-alt-left")!.Action);
        Assert.Equal(Defaults.Entries.Count, set.Entries.Count);
    }

    [Fact]
    public void New_mux_keys_and_prefix_keys_are_added_and_none_unbinds_prefix_keys()
    {
        var set = KeybindLegacy.Mux(
            Defaults,
            "ctrl+a",
            new Dictionary<string, string> { ["f f"] = "none", ["y"] = "zoom" },
            new Dictionary<string, string> { ["ctrl+g"] = "zoom", ["ctrl+x y"] = "zoom" });

        var mux = set.For(KeybindTarget.Mux, KeybindOs.Windows);
        Assert.Equal("Ctrl+a", set.Find(KeybindLegacy.MuxPrefixId)!.Chord);
        Assert.Contains(mux, b => b.Chord == "Ctrl+g" && b.Action == "zoom" && b.Contexts.SequenceEqual(["direct"]));
        Assert.Contains(mux, b => b.Chord == "y" && b.Action == "zoom" && b.Contexts.SequenceEqual(["prefix"]));
        Assert.DoesNotContain(mux, b => b.Chord == "f f");
        Assert.DoesNotContain(mux, b => b.Chord == "Ctrl+x y");
    }

    [Fact]
    public void Mux_keys_match_letters_under_modifiers_in_any_case_and_a_literal_space()
    {
        var set = KeybindLegacy.Mux(
            Defaults,
            null,
            new Dictionary<string, string> { [" "] = "zoom" },
            new Dictionary<string, string> { ["ctrl+H"] = "none" });

        var mux = set.For(KeybindTarget.Mux, KeybindOs.Windows);
        Assert.DoesNotContain(mux, b => b.Chord is "Ctrl+h" or "Ctrl+H");
        Assert.Equal("zoom", set.Find("mux.menu")!.Action);
    }

    [Fact]
    public void Mux_commands_with_arguments_keep_their_argument()
    {
        Assert.Equal("focus-left", KeybindActions.FromMux("smart-focus left"));
        Assert.Equal("resize-up", KeybindActions.FromMux(" resize up "));
        Assert.Equal("tab 3", KeybindActions.FromMux("tab 3"));
        Assert.Equal("zoom", KeybindActions.FromMux("zoom"));
    }
}
