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
}