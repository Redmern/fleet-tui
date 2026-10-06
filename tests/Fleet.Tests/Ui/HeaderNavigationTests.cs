using Fleet.Ui;
using Fleet.Ui.Models;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace Fleet.Tests.Ui;

public class HeaderNavigationTests
{
    private static FleetRow Row(string text) => FleetRow.Plain(text);

    // Settings-menu shape: a header before the first item and one in the middle.
    private static FleetList List()
    {
        var list = new FleetList();

        FleetRows.KeepOffSpacers(list);
        FleetKeys.ApplyMotions(list, Keymap.Default);
        FleetRows.Fill(
            list,
            [Row("a"), Row("b"), Row("c")],
            headersBefore: new Dictionary<int, FleetRow> { [0] = Row("one"), [2] = Row("two") });

        return list;
    }

    [Fact]
    public void A_header_is_a_row_but_not_an_item()
    {
        var source = (FleetRowSource)List().Source!;

        Assert.Equal(3, source.Items);
        Assert.Equal(5, source.Count);
        Assert.False(source.Holds(0));
        Assert.True(source.Holds(1));
        Assert.False(source.Holds(3));
        Assert.Equal(4, source.IndexOf(2));
    }

    [Fact]
    public void The_menu_opens_on_the_first_item_not_on_its_header()
    {
        var list = List();

        Assert.Equal(1, list.SelectedItem);
        Assert.Equal(0, FleetRows.Selected(list));
    }

    [Fact]
    public void Moving_down_skips_a_header()
    {
        var list = List();

        FleetRows.Select(list, 1);
        list.NewKeyDownEvent(new Key('j'));

        Assert.Equal(2, FleetRows.Selected(list));
    }

    [Fact]
    public void Moving_up_skips_a_header()
    {
        var list = List();

        FleetRows.Select(list, 2);
        list.NewKeyDownEvent(new Key('k'));

        Assert.Equal(1, FleetRows.Selected(list));
    }

    [Fact]
    public void Moving_up_from_the_first_item_stays_off_the_top_header()
    {
        var list = List();

        list.NewKeyDownEvent(new Key('k'));
        list.NewKeyDownEvent(new Key('g'));

        Assert.Equal(0, FleetRows.Selected(list));
        Assert.True(((FleetRowSource)list.Source!).Holds(list.SelectedItem ?? -1));
    }
}
