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
}
