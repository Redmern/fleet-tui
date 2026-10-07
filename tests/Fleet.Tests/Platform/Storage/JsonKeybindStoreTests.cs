using Fleet.Platform.Storage;
using Fleet.Shared.Keybinds;
using Fleet.Shared.Keybinds.Enums;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Keymap.Models;

namespace Fleet.Tests.Platform.Storage;

[Collection(ConfigHomeCollection.Name)]
public sealed class JsonKeybindStoreTests : ConfigHomeFixture
{
    private readonly List<string> _log = [];

    private KeybindSet Load() => new JsonKeybindStore(_log.Add).Load();

    [Fact]
    public void Without_any_file_the_defaults_are_used()
    {
        Assert.Equal(KeybindDefaults.Set.Entries, Load().Entries);
        Assert.Empty(_log);
    }

    [Fact]
    public void Both_old_files_are_migrated_and_the_keybinds_section_wins()
    {
        File.WriteAllText(
            FleetPaths.KeymapFile,
            """
            {
              "version": 1,
              "prefix": "Ctrl+B",
              "bindings": { "Refresh": "F5" },
              "keybinds": {
                "fleet-ui.refresh": { "chord": "F6" },
                "resize-left": { "chord": "none" }
              }
            }
            """);
        File.WriteAllText(
            JsonKeybindStore.MuxKeysFile,
            """{ "prefix": "ctrl+a", "keys": { "ctrl+g": "zoom" }, "prefixKeys": { "f f": "none" } }""");

        var set = Load();

        Assert.Equal("Ctrl+B", set.Find(KeybindLegacy.FleetUiPrefixId)!.Chord);
        Assert.Equal("F6", set.Find(KeybindLegacy.FleetUiId(FleetAction.Refresh))!.Chord);
        Assert.Equal("Ctrl+a", set.Find(KeybindLegacy.MuxPrefixId)!.Chord);
        var mux = set.For(KeybindTarget.Mux, KeybindOs.Windows);
        Assert.Contains(mux, b => b.Chord == "Ctrl+g" && b.Action == "zoom");
        Assert.DoesNotContain(mux, b => b.Chord == "f f");
        Assert.DoesNotContain(set.For(KeybindTarget.Nvim, KeybindOs.Windows), b => b.Id == "resize-left");
        Assert.Empty(_log);
    }

    [Fact]
    public void A_corrupt_mux_file_is_logged_and_skipped()
    {
        File.WriteAllText(JsonKeybindStore.MuxKeysFile, "{ not json");

        Assert.Equal(KeybindDefaults.Set.Entries, Load().Entries);
        Assert.Single(_log);
    }

    [Fact]
    public void Saving_the_fleet_ui_keymap_keeps_the_keybinds_section()
    {
        File.WriteAllText(
            FleetPaths.KeymapFile,
            """{ "version": 1, "prefix": "", "bindings": {}, "keybinds": { "resize-left": { "chord": "alt+shift+h" } } }""");

        new JsonKeymapStore().Save(KeymapConfig.Default.With(FleetAction.Refresh, "F5"));

        var set = Load();
        Assert.Equal("Alt+Shift+h", set.Find("resize-left")!.Chord);
        Assert.Equal("F5", set.Find(KeybindLegacy.FleetUiId(FleetAction.Refresh))!.Chord);
        Assert.Equal("F5", new JsonKeymapStore().Load().Bindings[FleetAction.Refresh]);
    }

    [Fact]
    public void A_malformed_keybinds_section_never_costs_the_fleet_ui_its_bindings_and_survives_a_save()
    {
        File.WriteAllText(
            FleetPaths.KeymapFile,
            """{ "version": 1, "prefix": "", "bindings": { "Refresh": "F5" }, "keybinds": { "x": { "targets": ["mux"] } } }""");

        Assert.Equal("F5", new JsonKeymapStore().Load().Bindings[FleetAction.Refresh]);
        Assert.Equal("F5", Load().Find(KeybindLegacy.FleetUiId(FleetAction.Refresh))!.Chord);
        Assert.Single(_log);

        new JsonKeymapStore().Save(KeymapConfig.Default.With(FleetAction.Close, "x"));

        Assert.Contains("\"targets\": [", File.ReadAllText(FleetPaths.KeymapFile), StringComparison.Ordinal);
    }

    [Fact]
    public void Saving_without_a_keybinds_section_does_not_write_one()
    {
        new JsonKeymapStore().Save(KeymapConfig.Default);

        Assert.DoesNotContain("keybinds", File.ReadAllText(FleetPaths.KeymapFile), StringComparison.Ordinal);
    }
}
