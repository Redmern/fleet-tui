using Fleet.Ui;

namespace Fleet.Tests.Ui;

public class FloatScreensTests
{
    [Fact]
    public void The_float_takes_the_title_and_size_of_the_screen_on_top_and_gets_the_one_below_back()
    {
        var titles = new List<string>();
        var sizes = new List<(int, int)>();
        var (title, fit) = (FloatScreens.ShowTitle, FloatScreens.Fit);
        FloatScreens.ShowTitle = titles.Add;
        FloatScreens.Fit = (cols, rows) =>
        {
            sizes.Add((cols, rows));
            return (cols - 2, rows - 2);
        };

        try
        {
            var menu = new FloatScreen("fleet menu", 40, 10);
            var confirm = new FloatScreen("Quit fleet?", 50, 6);

            Assert.Equal((38, 8), FloatScreens.Running(menu, true));
            Assert.Equal((48, 4), FloatScreens.Running(confirm, true));
            Assert.Equal((38, 8), FloatScreens.Running(confirm, false));
            Assert.Null(FloatScreens.Running(menu, false));

            Assert.Equal(["fleet menu", "Quit fleet?", "fleet menu"], titles);
            Assert.Equal([(40, 10), (50, 6), (40, 10)], sizes);
            Assert.Null(FloatScreens.Current);
        }
        finally
        {
            (FloatScreens.ShowTitle, FloatScreens.Fit) = (title, fit);
        }
    }

    [Fact]
    public void Every_screen_change_holds_the_float_before_it_is_fitted()
    {
        var calls = new List<string>();
        var (title, fit, hold) = (FloatScreens.ShowTitle, FloatScreens.Fit, FloatScreens.Hold);
        FloatScreens.ShowTitle = _ => { };
        FloatScreens.Fit = (cols, rows) =>
        {
            calls.Add($"fit {cols}x{rows}");
            return null;
        };
        FloatScreens.Hold = () => calls.Add("hold");

        try
        {
            var menu = new FloatScreen("fleet menu", 40, 10);
            var settings = new FloatScreen("settings", 50, 12);

            FloatScreens.Running(menu, true);
            FloatScreens.Running(menu, false);
            FloatScreens.Running(settings, true);
            FloatScreens.Running(settings, false);

            Assert.Equal(["hold", "fit 40x10", "hold", "hold", "fit 50x12", "hold"], calls);
        }
        finally
        {
            (FloatScreens.ShowTitle, FloatScreens.Fit, FloatScreens.Hold) = (title, fit, hold);
        }
    }

    [Fact]
    public void A_modal_lifts_to_its_size_a_nested_one_relifts_and_closing_the_last_drops_back_to_the_tile()
    {
        var calls = new List<string>();
        var (lift, drop) = (FloatScreens.Lift, FloatScreens.Drop);
        FloatScreens.Reset();
        FloatScreens.Lift = s =>
        {
            calls.Add($"lift {s.Title} {s.Cols}x{s.Rows}");
            return (s.Cols, s.Rows);
        };
        FloatScreens.Drop = () =>
        {
            calls.Add("drop");
            return (50, 28);
        };
        var dash = new FloatScreen("fleet — alpha");
        var card = new FloatScreen("New agent", 60, 14, Lifts: true);
        var picker = new FloatScreen("Base branch", 70, 20, Lifts: true);

        try
        {
            Assert.Null(FloatScreens.Lifting(dash, true));
            Assert.Equal((60, 14), FloatScreens.Lifting(card, true));
            Assert.True(FloatScreens.Lifted);
            Assert.Equal((70, 20), FloatScreens.Lifting(picker, true));
            Assert.Equal((60, 14), FloatScreens.Lifting(picker, false));
            Assert.Equal((50, 28), FloatScreens.Lifting(card, false));
            Assert.False(FloatScreens.Lifted);

            Assert.Equal(["lift New agent 60x14", "lift Base branch 70x20", "lift New agent 60x14", "drop"], calls);
        }
        finally
        {
            FloatScreens.Reset();
            (FloatScreens.Lift, FloatScreens.Drop) = (lift, drop);
        }
    }

    [Fact]
    public void A_screen_that_does_not_lift_drops_a_lift_below_it_and_a_refused_lift_stays_in_the_pane()
    {
        var calls = new List<string>();
        var (lift, drop) = (FloatScreens.Lift, FloatScreens.Drop);
        FloatScreens.Reset();
        var refuse = false;
        FloatScreens.Lift = s =>
        {
            calls.Add($"lift {s.Title}");
            return refuse ? null : (s.Cols, s.Rows);
        };
        FloatScreens.Drop = () =>
        {
            calls.Add("drop");
            return (50, 28);
        };
        var card = new FloatScreen("New agent", 60, 14, Lifts: true);
        var settings = new FloatScreen("Settings");

        try
        {
            FloatScreens.Lifting(card, true);
            Assert.Equal((50, 28), FloatScreens.Lifting(settings, true));
            Assert.False(FloatScreens.Lifted);
            Assert.Equal((60, 14), FloatScreens.Lifting(settings, false));
            FloatScreens.Lifting(card, false);

            refuse = true;
            calls.Clear();
            Assert.Null(FloatScreens.Lifting(card, true));
            Assert.False(FloatScreens.Lifted);
            Assert.Null(FloatScreens.Lifting(card, false));
            Assert.Equal(["lift New agent"], calls);
        }
        finally
        {
            FloatScreens.Reset();
            (FloatScreens.Lift, FloatScreens.Drop) = (lift, drop);
        }
    }
}
