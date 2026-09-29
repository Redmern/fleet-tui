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
}