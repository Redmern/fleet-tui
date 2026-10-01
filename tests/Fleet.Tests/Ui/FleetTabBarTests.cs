using Fleet.Ui;
using Terminal.Gui.Input;

namespace Fleet.Tests.Ui;

public class FleetTabBarTests
{
    [Fact]
    public void Clicking_a_tab_title_asks_for_that_tab()
    {
        var bar = new FleetTabBar(0, 0, ["Agents (1)", "Subs (0)", "Repositories (0)"]);
        var chosen = new List<int>();
        bar.Chosen += chosen.Add;

        bar.Root.SubViews.ElementAt(2).NewMouseEvent(new Mouse { Flags = MouseFlags.LeftButtonPressed, Position = new(1, 0) });

        Assert.Equal([2], chosen);
    }
}