using Fleet.Cli.Commands;

namespace Fleet.Tests.Cli;

public class SwitchProjectPlanTests
{
    [Theory]
    [InlineData(false, false, false, false, false, true)] // not open anywhere, lowercase: open here
    [InlineData(false, false, false, true, false, false)] // not open anywhere, uppercase: open new
    [InlineData(true, true, false, false, false, true)] // parked, lowercase: show here
    [InlineData(true, true, false, true, false, false)] // parked, uppercase: show new
    [InlineData(true, false, true, false, true, true)] // open in this window, lowercase: focus
    [InlineData(true, false, true, true, false, false)] // open in this window, uppercase: move new
    [InlineData(true, false, false, false, false, true)] // open elsewhere, lowercase: move here
    [InlineData(true, false, false, true, true, false)] // open elsewhere, uppercase: focus its window
    public void Resolves_the_window_action_from_state_and_key_case(
        bool dashOpen, bool parked, bool here, bool newWindow, bool expectFocus, bool expectIntoThisWindow)
    {
        var plan = SwitchProjectPlan.Resolve(dashOpen, parked, here, newWindow);

        Assert.Equal(expectFocus, plan.Focus);
        Assert.Equal(expectIntoThisWindow, plan.IntoThisWindow);
    }
}
