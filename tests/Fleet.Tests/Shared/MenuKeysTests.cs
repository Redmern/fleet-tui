using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;
using Fleet.Ui;

namespace Fleet.Tests.Shared;

public class MenuKeysTests
{
    private static IReadOnlyDictionary<FleetAction, string> Bindings =>
        Keymap.Default.Config.Bindings;

    [Fact]
    public void Every_entry_gets_a_key()
    {
        var entries = MenuKeys.Assign(
            [FleetAction.NewAgent, FleetAction.AddRepository, FleetAction.Close], Bindings);

        Assert.All(entries, e => Assert.False(string.IsNullOrEmpty(e.Key)));
        Assert.Equal(3, entries.Count);
    }

    [Fact]
    public void No_two_entries_share_a_key()
    {
        var entries = MenuKeys.Assign(
            [
                FleetAction.NewAgent,
                FleetAction.ChangeHarness,
                FleetAction.ToggleHidden,
                FleetAction.RemoveAgent,
                FleetAction.AddRepository,
                FleetAction.RemoveRepository,
                FleetAction.EditKeybinds,
                FleetAction.OpenProject,
                FleetAction.Close,
            ],
            Bindings);

        Assert.Equal(entries.Count, entries.Select(e => e.Key).Distinct().Count());
    }

    [Fact]
    public void An_action_keeps_the_key_it_already_has_elsewhere_in_fleet()
    {
        var entries = MenuKeys.Assign([FleetAction.NewAgent], Bindings);

        Assert.Equal("n", Assert.Single(entries).Key);
    }

    [Fact]
    public void A_key_already_taken_falls_back_to_a_letter_from_the_label()
    {
        var entries = MenuKeys.Assign(
            [FleetAction.NewAgent, FleetAction.AddRepository], Bindings);

        Assert.Equal("n", entries[0].Key);
        Assert.Equal("a", entries[1].Key);
    }

    [Fact]
    public void The_order_asked_for_is_the_order_shown()
    {
        var entries = MenuKeys.Assign([FleetAction.Close, FleetAction.NewAgent], Bindings);

        Assert.Equal(FleetAction.Close, entries[0].Action);
        Assert.Equal(FleetAction.NewAgent, entries[1].Action);
    }

    [Fact]
    public void Entries_carry_a_short_label_for_the_status_strip()
    {
        var entry = Assert.Single(MenuKeys.Assign([FleetAction.RemoveAgent], Bindings));

        Assert.Equal("manage", entry.Label);
    }
}
