using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Keymap.Models;
using Fleet.Ui;
using Terminal.Gui.Input;

namespace Fleet.Tests.Shared;

public class HeadKeymapTests
{
    [Theory]
    [InlineData(FleetAction.OpenHead, "Alt+O")]
    [InlineData(FleetAction.OpenHeadVoice, "Alt+Shift+O")]
    public void The_head_chords_ship_as_direct_alt_keys(FleetAction action, string key)
    {
        Assert.Equal(key, KeymapDefaults.Bindings[action]);
    }

    [Fact]
    public void Both_head_chords_are_rebindable_in_their_own_group()
    {
        var group = KeymapGroups.All.Single(g => g.Actions.Contains(FleetAction.OpenHead));

        Assert.Equal("anywhere, no prefix", group.Label);
        Assert.Equal([FleetAction.OpenHead, FleetAction.OpenHeadVoice], group.Actions);
        Assert.Contains(FleetAction.OpenHeadVoice, KeymapDefaults.Configurable);
    }

    [Fact]
    public void The_head_chords_parse_to_distinct_valid_keys()
    {
        var keymap = Keymap.Default;

        var plain = keymap.KeyFor(FleetAction.OpenHead);
        var voice = keymap.KeyFor(FleetAction.OpenHeadVoice);

        Assert.True(plain.IsValid);
        Assert.True(voice.IsValid);
        Assert.NotEqual(plain, voice);
        Assert.NotEqual(Key.Empty, voice);
    }

    [Fact]
    public void A_rebound_head_chord_survives_merging_over_the_defaults()
    {
        var config = KeymapConfig.Default.With(FleetAction.OpenHead, "Alt+H").MergedOverDefaults();

        Assert.Equal("Alt+H", config.Bindings[FleetAction.OpenHead]);
        Assert.Equal("Alt+Shift+O", config.Bindings[FleetAction.OpenHeadVoice]);
    }

    [Theory]
    [InlineData(FleetAction.OpenHead, "head")]
    [InlineData(FleetAction.OpenHeadVoice, "head-voice")]
    public void The_head_actions_have_stable_ids(FleetAction action, string id)
    {
        Assert.Equal(id, FleetActionIds.For(action));
        Assert.Equal(action, FleetActionIds.Parse(id));
    }
}
