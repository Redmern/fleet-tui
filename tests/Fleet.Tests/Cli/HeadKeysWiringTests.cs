using Fleet.Cli.Composition;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Keymap.Models;
using Fleet.Ui;

namespace Fleet.Tests.Cli;

public class HeadKeysWiringTests
{
    [Fact]
    public void Fleetd_gets_one_head_chord_that_opens_voice_mode()
    {
        var keys = EmbeddedWiring.HeadKeys(Keymap.Default);

        Assert.Equal("head voice", Assert.Single(keys).Value);
        Assert.Equal("head voice", keys["alt+o"]);
    }

    [Fact]
    public void A_head_chord_rebound_in_keybinds_reaches_fleetd()
    {
        var keys = EmbeddedWiring.HeadKeys(
            new Keymap(KeymapConfig.Default.With(FleetAction.OpenHeadVoice, "Ctrl+Alt+V")));

        Assert.Equal("head voice", keys["ctrl+alt+v"]);
        Assert.False(keys.ContainsKey("alt+o"));
    }
}
