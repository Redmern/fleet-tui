using Fleet.Ui;

namespace Fleet.Tests.Ui;

public class FleetKeysTests
{
    [Fact]
    public void Moving_past_the_last_row_lands_on_the_first()
    {
        Assert.Equal(0, FleetList.Wrap(3, 3));
        Assert.Equal(1, FleetList.Wrap(4, 3));
    }

    [Fact]
    public void Moving_up_from_the_first_row_lands_on_the_last()
    {
        Assert.Equal(2, FleetList.Wrap(-1, 3));
        Assert.Equal(1, FleetList.Wrap(-2, 3));
    }

    [Fact]
    public void An_empty_list_stays_at_zero_rather_than_dividing_by_zero()
    {
        Assert.Equal(0, FleetList.Wrap(-1, 0));
        Assert.Equal(0, FleetList.Wrap(5, 0));
    }
}
