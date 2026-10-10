using Fleet.Platform.Storage;
using Fleet.Shared.Iso.Models;

namespace Fleet.Tests.Platform.Storage;

[Collection(ConfigHomeCollection.Name)]
public sealed class JsonIsoModeTests : ConfigHomeFixture
{
    [Fact]
    public void Iso_mode_is_off_without_a_file() =>
        Assert.False(new JsonIsoMode().Load().On);

    [Fact]
    public void A_saved_config_round_trips()
    {
        new JsonIsoMode().Save(new IsoConfig(
            true,
            ["laptop.lan"],
            new Dictionary<string, string> { ["acme-portal"] = "alpha" }));

        var loaded = new JsonIsoMode().Load();

        Assert.True(loaded.On);
        Assert.Equal(["laptop.lan"], loaded.AttachFrom);
        Assert.Equal("alpha", loaded.Codes["ACME-portal"]);
    }

    [Fact]
    public void A_damaged_file_fails_closed()
    {
        File.WriteAllText(FleetPaths.IsoFile, "{ not json");

        var loaded = new JsonIsoMode().Load();

        Assert.True(loaded.On);
        Assert.Empty(loaded.AttachFrom);
    }

    [Fact]
    public void A_hand_edited_file_is_read_whatever_the_case_of_its_keys()
    {
        File.WriteAllText(FleetPaths.IsoFile, """{ "On": true, "AttachFrom": ["10.0.0.5"] }""");

        var loaded = new JsonIsoMode().Load();

        Assert.True(loaded.On);
        Assert.Equal(["10.0.0.5"], loaded.AttachFrom);
    }

    [Fact]
    public void The_cache_sees_a_change_to_the_file()
    {
        var cached = new CachedIsoMode(new JsonIsoMode());
        Assert.False(cached.Load().On);

        new JsonIsoMode().Save(IsoConfig.Off with { On = true });
        File.SetLastWriteTimeUtc(FleetPaths.IsoFile, DateTime.UtcNow.AddMinutes(1));

        Assert.True(cached.Load().On);
    }

    [Fact]
    public void A_toggle_shows_on_the_next_load_without_a_restart()
    {
        var iso = new JsonIsoMode();

        iso.Save(IsoConfig.Off with { On = true });
        Assert.True(iso.Load().On);

        iso.Save(IsoConfig.Off);
        Assert.False(iso.Load().On);
    }
}
