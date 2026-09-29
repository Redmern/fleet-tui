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
        FloatScreens.Fit = (cols, rows) => sizes.Add((cols, rows));

        try
        {
            var menu = new FloatScreen("fleet menu", 40, 10);
            var confirm = new FloatScreen("Quit fleet?", 50, 6);

            FloatScreens.Running(menu, true);
            FloatScreens.Running(confirm, true);
            FloatScreens.Running(confirm, false);
            FloatScreens.Running(menu, false);

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
    public void Closing_a_screen_holds_the_float_before_the_next_screen_is_fitted()
    {
        var calls = new List<string>();
        var (title, fit, hold) = (FloatScreens.ShowTitle, FloatScreens.Fit, FloatScreens.Hold);
        FloatScreens.ShowTitle = _ => { };
        FloatScreens.Fit = (cols, rows) => calls.Add($"fit {cols}x{rows}");
        FloatScreens.Hold = () => calls.Add("hold");

        try
        {
            var menu = new FloatScreen("fleet menu", 40, 10);
            var settings = new FloatScreen("settings", 50, 12);

            FloatScreens.Running(menu, true);
            FloatScreens.Running(menu, false);
            FloatScreens.Running(settings, true);
            FloatScreens.Running(settings, false);

            Assert.Equal(["fit 40x10", "hold", "fit 50x12", "hold"], calls);
        }
        finally
        {
            (FloatScreens.ShowTitle, FloatScreens.Fit, FloatScreens.Hold) = (title, fit, hold);
        }
    }
}