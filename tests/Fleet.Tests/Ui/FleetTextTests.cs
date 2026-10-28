using Fleet.Ui;

namespace Fleet.Tests.Ui;

public class FleetTextTests
{
    [Fact]
    public void Text_that_fits_is_left_alone()
    {
        Assert.Equal("main", FleetText.Middle("main", 10));
        Assert.Equal("main", FleetText.Middle("main", 4));
    }

    [Fact]
    public void A_long_name_keeps_its_start_and_its_end_around_an_ellipsis()
    {
        var name = "feature/ABC-2512-some-long-description-with-many-words";

        var cut = FleetText.Middle(name, 30);

        Assert.Equal(30, cut.Length);
        Assert.StartsWith("feature/ABC-2512-", cut);
        Assert.EndsWith("words", cut);
        Assert.Contains("…", cut);
    }

    [Fact]
    public void More_of_the_start_survives_than_of_the_end()
    {
        var cut = FleetText.Middle(new string('a', 40) + new string('b', 40), 31);
        var at = cut.IndexOf('…');

        Assert.True(at > cut.Length - at - 1);
    }

    [Fact]
    public void Tiny_widths_still_fit()
    {
        Assert.Equal("…", FleetText.Middle("develop", 1));
        Assert.Equal("d…", FleetText.Middle("develop", 2));
        Assert.Equal(string.Empty, FleetText.Middle("develop", 0));
    }

    [Theory]
    [InlineData(66, 120, 66)]
    [InlineData(66, 50, 48)]
    [InlineData(66, 10, 10)]
    [InlineData(0, 50, 0)]
    public void A_modal_size_is_clamped_to_the_screen_with_a_margin(int wanted, int screen, int expected)
    {
        Assert.Equal(expected, FleetTheme.Fit(wanted, screen));
    }
}
