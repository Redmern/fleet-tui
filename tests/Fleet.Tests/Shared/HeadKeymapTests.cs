using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Keymap.Models;
using Fleet.Ui;
using Terminal.Gui.Input;

namespace Fleet.Tests.Shared;

public class HeadKeymapTests
{
    [Fact]
    public void The_head_chord_ships_as_alt_o_and_opens_the_head_in_voice_mode()
    {
        Assert.Equal("Alt+o", KeymapDefaults.Bindings[FleetAction.OpenHeadVoice]);
        Assert.Contains("voice mode", KeymapDefaults.Describe(FleetAction.OpenHeadVoice));
    }

    [Fact]
    public void The_head_chord_is_rebindable_in_its_own_group()
    {
        var group = KeymapGroups.All.Single(g => g.Actions.Contains(FleetAction.OpenHeadVoice));

        Assert.Equal("anywhere, no prefix", group.Label);
        Assert.Equal([FleetAction.OpenHeadVoice], group.Actions);
        Assert.Contains(FleetAction.OpenHeadVoice, KeymapDefaults.Configurable);
    }

    [Fact]
    public void The_head_chord_parses_to_alt_o()
    {
        var voice = Keymap.Default.KeyFor(FleetAction.OpenHeadVoice);

        Assert.True(voice.IsValid);
        Assert.Equal(new Key("Alt+O"), voice);
    }

    [Fact]
    public void A_rebound_head_chord_survives_merging_over_the_defaults()
    {
        var config = KeymapConfig.Default.With(FleetAction.OpenHeadVoice, "Alt+H").MergedOverDefaults();

        Assert.Equal("Alt+H", config.Bindings[FleetAction.OpenHeadVoice]);
    }

    [Fact]
    public void The_head_action_keeps_its_id()
    {
        Assert.Equal("head-voice", FleetActionIds.For(FleetAction.OpenHeadVoice));
        Assert.Equal(FleetAction.OpenHeadVoice, FleetActionIds.Parse("head-voice"));
    }

    // The text-mode chord is gone; a keymap that rebound it (id "head") now
    // rebinds the one head chord.
    [Fact]
    public void The_old_text_mode_id_reads_as_the_head_chord()
        => Assert.Equal(FleetAction.OpenHeadVoice, FleetActionIds.Parse("head"));
}
