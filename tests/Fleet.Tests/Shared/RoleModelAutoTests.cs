using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Models;

namespace Fleet.Tests.Shared;

public class RoleModelAutoTests
{
    [Theory]
    [InlineData("auto")]
    [InlineData(" AUTO ")]
    [InlineData("Auto")]
    public void Auto_in_any_case_is_kept_as_the_auto_sentinel(string raw)
    {
        Assert.Equal(ModelChoice.Auto, ModelChoice.Model(raw));
        Assert.True(ModelChoice.IsAuto(raw));
    }

    [Fact]
    public void A_concrete_model_is_not_auto()
    {
        Assert.False(ModelChoice.IsAuto("sonnet"));
        Assert.False(new RoleModel("sonnet", "medium").IsAuto);
    }

    [Fact]
    public void An_auto_role_passes_no_model_or_effort_flags_to_claude()
    {
        var role = new RoleModel(ModelChoice.Auto, "high");

        Assert.True(role.IsAuto);
        Assert.Empty(role.Arguments);
    }
}
