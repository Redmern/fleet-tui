using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Keymap.Models;
using Fleet.Ui;
using Terminal.Gui.Input;

namespace Fleet.Tests.Ui;

public class KeymapTests
{
    [Fact]
    public void The_default_prefix_is_ctrl_enter()
        => Assert.Equal(Key.Enter.WithCtrl, Keymap.Default.Prefix);

    [Fact]
    public void The_default_prefix_avoids_the_keys_neovim_and_wezterm_claim()
    {
        Assert.NotEqual(Key.S.WithCtrl, Keymap.Default.Prefix);
        Assert.NotEqual(Key.Space.WithCtrl, Keymap.Default.Prefix);
        Assert.NotEqual(Key.D.WithCtrl, Keymap.Default.Prefix);
    }

    [Fact]
    public void The_default_prefix_parses_rather_than_falling_back()
    {
        Assert.True(Keymap.Default.Prefix.IsValid);
        Assert.Equal(KeymapDefaults.Prefix, Keymap.Default.PrefixText);
    }

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
        Assert.Equal(FleetAction.Refresh, Keymap.Default.ActionFor(Key.R));
    }

    [Fact]
    public void A_saved_binding_for_a_retired_action_is_dropped_rather_than_crashing()
    {
        var stale = new KeymapConfig(
            KeymapDefaults.Prefix,
            new Dictionary<FleetAction, string> { [FleetAction.StopAgent] = "a" });

        var keymap = new Keymap(stale);

        Assert.Equal(Key.Empty, keymap.KeyFor(FleetAction.StopAgent));
        Assert.Equal(FleetAction.None, keymap.ActionFor(Key.A));
    }

    [Fact]
    public void A_scope_decides_which_action_a_shared_key_means()
    {
        FleetAction[] dashboard = [FleetAction.NextTab];
        FleetAction[] picker = [FleetAction.OpenProject];

        Assert.Equal(FleetAction.NextTab, Keymap.Default.ActionFor(Key.L, dashboard));
        Assert.Equal(FleetAction.OpenProject, Keymap.Default.ActionFor(Key.L, picker));
    }

    [Fact]
    public void A_key_outside_the_scope_resolves_to_nothing()
    {
        FleetAction[] scope = [FleetAction.Close];

        Assert.Equal(FleetAction.None, Keymap.Default.ActionFor(Key.J, scope));
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

        Assert.Equal(Key.Enter.WithCtrl, keymap.Prefix);
        Assert.Equal(Key.J, keymap.KeyFor(FleetAction.MoveDown));
        Assert.Equal(new Key("F5"), keymap.KeyFor(FleetAction.Refresh));
    }
}
