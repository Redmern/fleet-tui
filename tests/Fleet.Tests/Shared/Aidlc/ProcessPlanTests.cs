using Fleet.Shared.Aidlc;
using Fleet.Shared.Aidlc.Enums;

namespace Fleet.Tests.Shared.Aidlc;

public class ProcessPlanTests
{
    [Fact]
    public void With_everything_on_the_plan_is_the_profile_as_catalogued()
    {
        var plan = ProcessPlan.Resolve(Profile.Feature, Autonomy.Guided, AidlcPart.None);

        Assert.Equal(ProfileCatalog.StagesOf(Profile.Feature), plan.Stages.Select(s => s.Stage));
        Assert.All(plan.Stages, s => Assert.Equal(StageState.Pending, s.State));
        Assert.Equal(ProfileCatalog.GatesOf(Profile.Feature), plan.Gated.Select(s => s.Stage));
        Assert.True(plan.WalkingSkeleton);
        Assert.Empty(plan.Skipped);
    }

    [Theory]
    [InlineData(AidlcPart.SpecGate, Stage.Specify)]
    [InlineData(AidlcPart.PlanGate, Stage.Plan)]
    [InlineData(AidlcPart.DeliverGate, Stage.Deliver)]
    [InlineData(AidlcPart.WalkingSkeleton, Stage.Build)]
    public void A_gate_switched_off_keeps_the_stage_but_makes_its_gate_automatic(AidlcPart part, Stage stage)
    {
        var plan = ProcessPlan.Resolve(Profile.Feature, Autonomy.Guided, part);

        var entry = plan.For(stage)!;

        Assert.True(entry.Runs);
        Assert.False(entry.HumanGate);
        Assert.Equal(StageState.Pending, entry.State);
    }

    [Theory]
    [InlineData(AidlcPart.Verify, Stage.Verify)]
    [InlineData(AidlcPart.Review, Stage.Review)]
    [InlineData(AidlcPart.Learn, Stage.Learn)]
    public void Verify_review_or_learn_switched_off_is_skipped_with_the_reason(AidlcPart part, Stage stage)
    {
        var plan = ProcessPlan.Resolve(Profile.Feature, Autonomy.Guided, part);

        var entry = plan.For(stage)!;

        Assert.Equal(StageState.Skipped, entry.State);
        Assert.Equal(ProcessPlan.OffInSettings, entry.Reason);
        Assert.Contains(entry, plan.Skipped);
    }

    [Fact]
    public void Switching_off_a_part_the_profile_does_not_have_changes_nothing()
    {
        var plain = ProcessPlan.Resolve(Profile.Express, Autonomy.Guided, AidlcPart.None);
        var off = ProcessPlan.Resolve(Profile.Express, Autonomy.Guided, AidlcPart.PlanGate | AidlcPart.Learn);

        Assert.Equal(plain.Stages, off.Stages);
    }

    [Fact]
    public void Without_the_walking_skeleton_build_has_no_human_gate()
    {
        var plan = ProcessPlan.Resolve(Profile.Feature, Autonomy.Automatic, AidlcPart.WalkingSkeleton);

        Assert.False(plan.WalkingSkeleton);
        Assert.Equal(Autonomy.Automatic, plan.Autonomy);
    }

    [Fact]
    public void Everything_off_leaves_a_plan_with_no_human_gates()
    {
        var everything = AidlcPart.SpecGate | AidlcPart.PlanGate | AidlcPart.DeliverGate | AidlcPart.Verify
            | AidlcPart.Review | AidlcPart.Learn | AidlcPart.WalkingSkeleton;

        var plan = ProcessPlan.Resolve(Profile.Feature, Autonomy.Guided, everything);

        Assert.Empty(plan.Gated);
        Assert.Equal([Stage.Verify, Stage.Review, Stage.Learn], plan.Skipped.Select(s => s.Stage));
    }
}
