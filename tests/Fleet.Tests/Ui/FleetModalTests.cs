using Fleet.Ui;

namespace Fleet.Tests.Ui;

public class FleetModalTests
{
    [Fact]
    public void Nothing_is_open_until_a_view_claims_the_keys()
    {
        Assert.False(FleetModal.Any);

        var claim = FleetModal.Enter();

        Assert.True(FleetModal.Any);
        Assert.True(FleetModal.Owns(claim));

        FleetModal.Leave();

        Assert.False(FleetModal.Any);
    }

    [Fact]
    public void Only_the_innermost_view_owns_the_keys()
    {
        var outer = FleetModal.Enter();
        var inner = FleetModal.Enter();

        Assert.True(FleetModal.Owns(inner));
        Assert.False(FleetModal.Owns(outer));

        FleetModal.Leave();

        Assert.True(FleetModal.Owns(outer));

        FleetModal.Leave();
    }

    [Fact]
    public void Backspace_on_a_screen_tells_its_caller_to_go_back_one_level()
    {
        FleetModal.Enter();
        FleetModal.Back();
        FleetModal.Leave();

        Assert.True(FleetModal.WentBack());
        Assert.False(FleetModal.WentBack());
    }

    [Fact]
    public void A_screen_closed_with_esc_does_not_go_back()
    {
        FleetModal.Enter();
        FleetModal.Leave();

        Assert.False(FleetModal.WentBack());
    }

    // Backspace in a dialog opened from a screen only closes that dialog; the
    // screen stays open, and when it is later closed with esc the menu must not
    // treat that as going back.
    [Fact]
    public void Going_back_out_of_a_nested_dialog_does_not_reach_the_menu()
    {
        FleetModal.Enter();
        FleetModal.Enter();
        FleetModal.Back();
        FleetModal.Leave();
        FleetModal.Leave();

        Assert.False(FleetModal.WentBack());
    }

    [Fact]
    public void A_screen_goes_back_after_a_nested_dialog_went_back()
    {
        FleetModal.Enter();
        FleetModal.Enter();
        FleetModal.Back();
        FleetModal.Leave();
        FleetModal.Back();
        FleetModal.Leave();

        Assert.True(FleetModal.WentBack());
    }

    [Fact]
    public void A_new_screen_forgets_an_earlier_back_nobody_asked_about()
    {
        FleetModal.Enter();
        FleetModal.Back();
        FleetModal.Leave();

        FleetModal.Enter();
        FleetModal.Leave();

        Assert.False(FleetModal.WentBack());
    }
}
