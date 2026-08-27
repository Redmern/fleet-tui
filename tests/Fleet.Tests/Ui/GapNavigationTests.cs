using Fleet.Ui;
using Fleet.Ui.Models;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace Fleet.Tests.Ui;

public class GapNavigationTests
{
    private static FleetRow Row(string text) => FleetRow.Plain(text);

    private static FleetList List()
    {
        var list = new FleetList();

        FleetRows.KeepOffSpacers(list);
        FleetKeys.ApplyMotions(list, Keymap.Default);
        FleetRows.Fill(list, [Row("a"), Row("b"), Row("c")], gapsAfter: [1]);

        return list;
    }

    [Fact]
    public void Moving_down_skips_the_gap()
    {
        var list = List();

        FleetRows.Select(list, 1);
        list.NewKeyDownEvent(new Key('j'));

        Assert.Equal(2, FleetRows.Selected(list));
        Assert.True(((FleetRowSource)list.Source!).Holds(list.SelectedItem ?? -1));
    }

    [Fact]
    public void Moving_up_skips_the_gap()
    {
        var list = List();

        FleetRows.Select(list, 2);
        list.NewKeyDownEvent(new Key('k'));

        Assert.Equal(1, FleetRows.Selected(list));
        Assert.True(((FleetRowSource)list.Source!).Holds(list.SelectedItem ?? -1));
    }

    [Fact]
    public void Walking_the_whole_list_never_rests_on_a_gap()
    {
        var list = new FleetList();

        FleetRows.KeepOffSpacers(list);
        FleetKeys.ApplyMotions(list, Keymap.Default);
        FleetRows.Fill(
            list,
            [Row("sub-a"), Row("child-1"), Row("child-2"), Row("sub-b"), Row("child-3")],
            gapsAfter: [2]);

        var source = (FleetRowSource)list.Source!;
        var seen = new List<int>();

        for (var i = 0; i < 12; i++)
        {
            list.NewKeyDownEvent(new Key('j'));
            Assert.True(source.Holds(list.SelectedItem ?? -1));
            seen.Add(FleetRows.Selected(list));
        }

        Assert.Contains(3, seen);

        list.NewKeyDownEvent(new Key('g'));
        Assert.Equal(0, FleetRows.Selected(list));

        list.NewKeyDownEvent(new Key(Key.G.WithShift));
        Assert.Equal(4, FleetRows.Selected(list));
        Assert.True(source.Holds(list.SelectedItem ?? -1));
    }
}
