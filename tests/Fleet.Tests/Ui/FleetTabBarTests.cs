using Fleet.Ui;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Fleet.Tests.Ui;

public class FleetTabBarTests
{
    [Fact]
    public void Clicking_a_tab_title_asks_for_that_tab()
    {
        var bar = new FleetTabBar(0, 0, ["Agents (1)", "Subs (0)", "Repositories (0)"]);
        var chosen = new List<int>();
        bar.Chosen += chosen.Add;

        Tab(bar, 2).NewMouseEvent(new Mouse { Flags = MouseFlags.LeftButtonPressed, Position = new(1, 0) });

        Assert.Equal([2], chosen);
    }

    private static readonly string[] Many =
        ["All (3)", "alpha (1)", "bravo (0)", "charlie (2)", "delta (0)", "echo (0)", "History (9)"];

    [Fact]
    public void Everything_renders_unscrolled_when_it_fits()
    {
        var bar = new FleetTabBar(0, 0, ["Open (2)", "Hidden (1)"]);
        Host(bar, 80);

        Assert.Equal(0, Tab(bar, 0).Frame.X);
        Assert.Equal("Open (2)".Length + 4, Tab(bar, 1).Frame.X);
        Assert.Equal(0, Strip(bar).Frame.X);
        Assert.False(Marker(bar, "‹").Visible);
        Assert.False(Marker(bar, "›").Visible);
    }

    [Fact]
    public void Moving_right_past_the_edge_scrolls_the_selected_tab_into_view()
    {
        var bar = new FleetTabBar(0, 0, Many);
        Host(bar, 40);

        for (var i = 0; i < Many.Length; i++)
        {
            bar.Select(i);
            Relayout(bar);
            AssertShown(bar, i);
        }

        Assert.True(Marker(bar, "‹").Visible);
        Assert.False(Marker(bar, "›").Visible);
    }

    [Fact]
    public void Moving_left_back_scrolls_the_selected_tab_into_view()
    {
        var bar = new FleetTabBar(0, 0, Many);
        Host(bar, 40);
        bar.Select(Many.Length - 1);

        for (var i = Many.Length - 1; i >= 0; i--)
        {
            bar.Select(i);
            Relayout(bar);
            AssertShown(bar, i);
        }

        Assert.False(Marker(bar, "‹").Visible);
        Assert.True(Marker(bar, "›").Visible);
    }

    [Fact]
    public void Wrapping_both_ways_keeps_the_selected_tab_in_view()
    {
        var bar = new FleetTabBar(0, 0, Many);
        Host(bar, 40);

        bar.Select((0 - 1 + Many.Length) % Many.Length);
        Relayout(bar);
        AssertShown(bar, Many.Length - 1);

        bar.Select((Many.Length - 1 + 1) % Many.Length);
        Relayout(bar);
        AssertShown(bar, 0);
    }

    [Fact]
    public void A_longer_title_keeps_the_selected_tab_in_view()
    {
        var bar = new FleetTabBar(0, 0, Many);
        Host(bar, 40);
        bar.Select(Many.Length - 1);
        Relayout(bar);

        bar.Retitle(Many.Length - 1, "History (12345)");
        bar.Retitle(1, "alpha (100000)");
        Relayout(bar);

        AssertShown(bar, Many.Length - 1);
    }

    [Fact]
    public void A_narrow_bar_still_shows_the_start_of_the_selected_tab()
    {
        var bar = new FleetTabBar(0, 0, Many);
        Host(bar, 10);

        for (var i = 0; i < Many.Length; i++)
        {
            bar.Select(i);
            Relayout(bar);
            Assert.Equal(0, Tab(bar, i).Frame.X);
        }
    }

    [Fact]
    public void Resizing_the_pane_keeps_the_selected_tab_in_view()
    {
        var bar = new FleetTabBar(0, 0, Many);
        var host = Host(bar, 120);
        bar.Select(Many.Length - 1);
        Relayout(bar);

        host.Width = 40;
        Relayout(bar);

        AssertShown(bar, Many.Length - 1);
    }

    [Fact]
    public void Clicking_a_scrolled_tab_still_asks_for_that_tab()
    {
        var bar = new FleetTabBar(0, 0, Many);
        Host(bar, 40);
        bar.Select(Many.Length - 1);
        Relayout(bar);
        var chosen = new List<int>();
        bar.Chosen += chosen.Add;

        Tab(bar, Many.Length - 1).NewMouseEvent(new Mouse { Flags = MouseFlags.LeftButtonPressed, Position = new(1, 0) });

        Assert.Equal([Many.Length - 1], chosen);
    }

    private static View Host(FleetTabBar bar, int width)
    {
        var host = new View { Width = width, Height = 2 };
        host.Add(bar.Root);
        host.Layout();
        Relayout(bar);
        return host;
    }

    private static void Relayout(FleetTabBar bar)
    {
        var host = bar.Root.SuperView!;
        host.SetNeedsLayout();
        host.Layout();
    }

    private static View Tab(FleetTabBar bar, int index) =>
        Strip(bar).SubViews.ElementAt(index);

    private static View Strip(FleetTabBar bar) =>
        bar.Root.SubViews.First(v => v is not Label);

    private static View Marker(FleetTabBar bar, string glyph) =>
        bar.Root.SubViews.OfType<Label>().First(l => l.Text == glyph);

    private static void AssertShown(FleetTabBar bar, int index)
    {
        var frame = Tab(bar, index).Frame;
        Assert.InRange(frame.X, 0, int.MaxValue);
        Assert.InRange(frame.Right, 0, Strip(bar).Viewport.Width);
    }
}
