using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Keymap.Models;
using Fleet.Ui;
using Terminal.Gui.Input;

namespace Fleet.Tests.Ui;

public class KeymapTests
{
    [Fact]
    public void The_default_prefix_is_ctrl_s()
        => Assert.Equal(Key.S.WithCtrl, Keymap.Default.Prefix);

    [Fact]
    public void The_default_menu_key_is_space()
        => Assert.Equal(Key.Space, Keymap.Default.KeyFor(FleetAction.OpenMenu));

    [Fact]
    public void Escape_is_never_bound_to_an_action()
        => Assert.Equal(FleetAction.None, Keymap.Default.ActionFor(Key.Esc));

    [Fact]
    public void Every_configurable_action_has_a_default_binding()
    {
        foreach (var action in KeymapDefaults.Configurable)
        {
            Assert.True(
                Keymap.Default.KeyFor(action).IsValid,
                $"{action} has no valid default binding");
        }
    }

    [Fact]
    public void ActionFor_resolves_a_bound_key()
    {
        Assert.Equal(FleetAction.Close, Keymap.Default.ActionFor(Key.Q));
        Assert.Equal(FleetAction.AddRepository, Keymap.Default.ActionFor(Key.A));
    }

    [Fact]
    public void A_custom_binding_overrides_the_default()
    {
        var keymap = new Keymap(KeymapConfig.Default.With(FleetAction.Close, "x"));

        Assert.Equal(Key.X, keymap.KeyFor(FleetAction.Close));
        Assert.Equal(FleetAction.Close, keymap.ActionFor(Key.X));
    }

    [Fact]
    public void A_custom_prefix_overrides_the_default()
    {
        var keymap = new Keymap(KeymapConfig.Default.WithPrefix("Ctrl+B"));

        Assert.Equal(Key.B.WithCtrl, keymap.Prefix);
    }

    [Fact]
    public void An_unparseable_binding_falls_back_to_the_default()
    {
        var keymap = new Keymap(KeymapConfig.Default.With(FleetAction.Close, "!!not-a-key!!"));

        Assert.Equal(Key.Q, keymap.KeyFor(FleetAction.Close));
    }

    [Fact]
    public void A_partial_config_is_merged_over_the_defaults()
    {
        var partial = new KeymapConfig(
            string.Empty,
            new Dictionary<FleetAction, string> { [FleetAction.Refresh] = "F5" });

        var keymap = new Keymap(partial);

        Assert.Equal(Key.S.WithCtrl, keymap.Prefix);
        Assert.Equal(Key.J, keymap.KeyFor(FleetAction.MoveDown));
        Assert.Equal(new Key("F5"), keymap.KeyFor(FleetAction.Refresh));
    }
}
