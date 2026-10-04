using Fleet.Ui;
using Terminal.Gui.Input;

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

    [Fact]
    public void Backspace_goes_back_when_nothing_is_typed()
    {
        Assert.True(FleetKeys.GoesBack(Key.Backspace));
        Assert.True(FleetKeys.GoesBack(Key.Backspace, string.Empty));
    }

    [Fact]
    public void Backspace_in_a_field_with_text_deletes_instead_of_going_back()
    {
        Assert.False(FleetKeys.GoesBack(Key.Backspace, "a"));
        Assert.False(FleetKeys.GoesBack(Key.Backspace, "my-repo"));
    }

    [Fact]
    public void Only_a_bare_backspace_goes_back()
    {
        Assert.False(FleetKeys.GoesBack(Key.Esc));
        Assert.False(FleetKeys.GoesBack(Key.H));
        Assert.False(FleetKeys.GoesBack(Key.Backspace.WithShift));
        Assert.False(FleetKeys.GoesBack(Key.Backspace.WithCtrl));
    }
}
