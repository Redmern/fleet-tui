using Fleet.Shared.Aidlc;
using Fleet.Shared.Aidlc.Enums;

namespace Fleet.Tests.Shared.Aidlc;

public class ProfileCatalogTests
{
    [Fact]
    public void There_are_five_profiles_and_express_is_the_default()
    {
        Assert.Equal(5, ProfileCatalog.All.Count);
        Assert.Equal(Profile.Express, ProfileCatalog.Default);
    }

    [Fact]
    public void Express_is_the_short_flow_with_only_the_merge_gate()
    {
        Assert.Equal(
            [Stage.Intake, Stage.Specify, Stage.Build, Stage.Verify, Stage.Review, Stage.Deliver],
            ProfileCatalog.StagesOf(Profile.Express));
        Assert.Equal([Stage.Deliver], ProfileCatalog.GatesOf(Profile.Express));
    }

    [Fact]
    public void Bugfix_discovers_and_gates_the_spec_and_the_merge()
    {
        Assert.Equal(
            [Stage.Intake, Stage.Discover, Stage.Specify, Stage.Build, Stage.Verify, Stage.Review, Stage.Deliver],
            ProfileCatalog.StagesOf(Profile.Bugfix));
        Assert.Equal([Stage.Specify, Stage.Deliver], ProfileCatalog.GatesOf(Profile.Bugfix));
    }

    [Fact]
    public void Refactor_plans_instead_of_specifying()
    {
        Assert.DoesNotContain(Stage.Specify, ProfileCatalog.StagesOf(Profile.Refactor));
        Assert.Contains(Stage.Plan, ProfileCatalog.StagesOf(Profile.Refactor));
        Assert.Equal([Stage.Plan, Stage.Deliver], ProfileCatalog.GatesOf(Profile.Refactor));
    }

    [Fact]
    public void Feature_runs_every_stage_with_four_gates()
    {
        Assert.Equal(Enum.GetValues<Stage>(), ProfileCatalog.StagesOf(Profile.Feature));
        Assert.Equal(
            [Stage.Specify, Stage.Plan, Stage.Build, Stage.Deliver],
            ProfileCatalog.GatesOf(Profile.Feature));
    }

    [Fact]
    public void Research_discovers_and_reports_without_building()
    {
        Assert.Equal([Stage.Intake, Stage.Discover, Stage.Deliver], ProfileCatalog.StagesOf(Profile.Research));
        Assert.Equal([Stage.Deliver], ProfileCatalog.GatesOf(Profile.Research));
    }

    [Theory]
    [InlineData(Profile.Express)]
    [InlineData(Profile.Bugfix)]
    [InlineData(Profile.Feature)]
    [InlineData(Profile.Refactor)]
    [InlineData(Profile.Research)]
    public void Every_profile_starts_with_intake_keeps_stage_order_and_gates_only_its_own_stages(Profile profile)
    {
        var stages = ProfileCatalog.StagesOf(profile);

        Assert.Equal(Stage.Intake, stages[0]);
        Assert.Equal(stages.OrderBy(s => s), stages);
        Assert.All(ProfileCatalog.GatesOf(profile), g => Assert.Contains(g, stages));
        Assert.False(ProfileCatalog.IsGated(profile, Stage.Intake));
        Assert.NotEmpty(ProfileCatalog.Describe(profile));
    }
}
