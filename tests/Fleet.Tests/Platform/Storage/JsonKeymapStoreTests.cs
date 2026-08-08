using Fleet.Platform.Storage;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Keymap.Models;

namespace Fleet.Tests.Platform.Storage;

[Collection(ConfigHomeCollection.Name)]
public sealed class JsonKeymapStoreTests : ConfigHomeFixture
{
    private static JsonKeymapStore Store => new();

    [Fact]
    public void Load_returns_the_defaults_when_nothing_is_saved()
    {
        var config = Store.Load();

        Assert.Equal("Ctrl+S", config.Prefix);
        Assert.Equal("Space", config.Bindings[FleetAction.OpenMenu]);
    }

    [Fact]
    public void Save_then_Load_round_trips_a_custom_binding()
    {
        Store.Save(KeymapConfig.Default.With(FleetAction.AddRepository, "F2"));

        Assert.Equal("F2", Store.Load().Bindings[FleetAction.AddRepository]);
    }

    [Fact]
    public void Save_then_Load_round_trips_a_custom_prefix()
    {
        Store.Save(KeymapConfig.Default.WithPrefix("Ctrl+B"));

        Assert.Equal("Ctrl+B", Store.Load().Prefix);
    }

    [Fact]
    public void Unsaved_actions_keep_their_defaults()
    {
        Store.Save(new KeymapConfig(
            "Ctrl+B",
            new Dictionary<FleetAction, string> { [FleetAction.Refresh] = "F5" }));

        var config = Store.Load();

        Assert.Equal("F5", config.Bindings[FleetAction.Refresh]);
        Assert.Equal("j", config.Bindings[FleetAction.MoveDown]);
    }

    [Fact]
    public void A_corrupt_file_falls_back_to_the_defaults()
    {
        FleetPaths.EnsureDirs();
        File.WriteAllText(FleetPaths.KeymapFile, "{ not json");

        Assert.Equal("Ctrl+S", Store.Load().Prefix);
    }

    [Fact]
    public void An_unknown_action_name_in_the_file_is_ignored()
    {
        FleetPaths.EnsureDirs();
        File.WriteAllText(
            FleetPaths.KeymapFile,
            """{"version":1,"prefix":"Ctrl+B","bindings":{"NotARealAction":"z","Close":"x"}}""");

        var config = Store.Load();

        Assert.Equal("x", config.Bindings[FleetAction.Close]);
        Assert.Equal("Ctrl+B", config.Prefix);
    }
}
