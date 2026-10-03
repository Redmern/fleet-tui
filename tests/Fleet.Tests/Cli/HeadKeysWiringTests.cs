using Fleet.Cli.Composition;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Keymap.Models;
using Fleet.Ui;

namespace Fleet.Tests.Cli;

public class HeadKeysWiringTests
{
    [Fact]
    public void Fleetd_gets_the_head_chords_from_the_fleet_keymap()
    {
        var keys = EmbeddedWiring.HeadKeys(Keymap.Default);

        Assert.Equal("head", keys["alt+o"]);
        Assert.Equal("head voice", keys["alt+shift+o"]);
    }

    [Fact]
    public void A_head_chord_rebound_in_keybinds_reaches_fleetd()
    {
        var keys = EmbeddedWiring.HeadKeys(
            new Keymap(KeymapConfig.Default.With(FleetAction.OpenHeadVoice, "Ctrl+Alt+V")));

        Assert.Equal("head voice", keys["ctrl+alt+v"]);
        Assert.False(keys.ContainsKey("alt+shift+o"));
    }
}
