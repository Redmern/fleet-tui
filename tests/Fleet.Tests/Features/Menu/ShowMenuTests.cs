using Fleet.Features.Menu.ShowMenu;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Keymap.Models;
using Fleet.Ui;

namespace Fleet.Tests.Features.Menu;

public class ShowMenuTests
{
    private static readonly FleetAction[] DashboardActions =
    [
        FleetAction.AddRepository,
        FleetAction.Refresh,
        FleetAction.EditKeybinds,
        FleetAction.Close,
    ];

    [Fact]
    public void Every_requested_action_becomes_a_menu_item()
    {
        var items = new ShowMenuHandler(Keymap.Default).Items(DashboardActions);

        Assert.Equal(DashboardActions, items.Select(i => i.Action));
    }

    [Fact]
    public void Each_item_shows_its_current_keybind()
    {
        var items = new ShowMenuHandler(Keymap.Default).Items(DashboardActions);

        Assert.Equal("a", items.Single(i => i.Action == FleetAction.AddRepository).KeyText);
        Assert.Equal("q", items.Single(i => i.Action == FleetAction.Close).KeyText);
    }

    [Fact]
    public void Each_item_has_a_human_label()
    {
        var items = new ShowMenuHandler(Keymap.Default).Items(DashboardActions);

        Assert.All(items, i => Assert.False(string.IsNullOrWhiteSpace(i.Label)));
        Assert.Equal("Add repository", items[0].Label);
    }

    [Fact]
    public void A_rebound_action_shows_its_new_key_in_the_menu()
    {
        var keymap = new Keymap(KeymapConfig.Default.With(FleetAction.AddRepository, "F2"));

        var items = new ShowMenuHandler(keymap).Items(DashboardActions);

        Assert.Equal("f2", items.Single(i => i.Action == FleetAction.AddRepository).KeyText);
    }

    [Fact]
    public void Rows_are_aligned_on_the_longest_label()
    {
        var items = new ShowMenuHandler(Keymap.Default).Items(DashboardActions);

        var rows = ShowMenuHandler.Rows(items);

        Assert.Equal(items.Count, rows.Count);

        var keyColumns = rows
            .Select((row, i) => row.Length - items[i].KeyText.Length)
            .Distinct();

        Assert.Single(keyColumns);
    }

    [Fact]
    public void No_items_produces_no_rows()
        => Assert.Empty(ShowMenuHandler.Rows([]));
}
