using Fleet.Features.Orchestrations.Flags;
using Fleet.Shared.Aidlc.Enums;
using Fleet.Shared.Aidlc.Models;

namespace Fleet.Tests.Features.Orchestrations.Flags;

// AC-18: a unit started while the walking-skeleton gate is not approved. Fleet writes no "approved" flag, so the
// gate counts as approved when the skeleton unit is done (or skipped) or the Build stage is done (or skipped).
public sealed class SkeletonGateTests
{
    private static UnitEntry Unit(string id, string branch, UnitState state, bool skeleton = false, string repository = "api") =>
        new(new WorkUnit(id, $"unit {id}", repository, branch, [], [], [], "dotnet test", skeleton), state);

    private static IntentState State(StageState build, params UnitEntry[] units) =>
        new(
            "upgrade",
            Profile.Feature,
            Autonomy.Guided,
            [new StagePlan(Stage.Plan, true, StageState.Done), new StagePlan(Stage.Build, true, build)],
            units,
            "2026-11-17T00:00:00Z",
            "2026-11-17T00:00:00Z");

    private static IntentState Building(UnitState skeleton = UnitState.Building) =>
        State(
            StageState.Active,
            Unit("u1", "feat/skeleton", skeleton, skeleton: true),
            Unit("u2", "feat/second", UnitState.Blocked));

    [Fact]
    public void A_unit_started_while_the_skeleton_is_still_building_gets_a_notice()
    {
        var line = SkeletonGate.EarlyStart(Building(), "api", "feat/second");

        Assert.NotNull(line);
        Assert.Contains("unit started before the skeleton gate was approved", line);
        Assert.Contains("api/feat/second (unit u2)", line);
        Assert.Contains("skeleton u1 is building", line);
        Assert.Contains("Nothing was blocked", line);
    }

    [Theory]
    [InlineData(UnitState.Blocked)]
    [InlineData(UnitState.Ready)]
    [InlineData(UnitState.Reviewing)]
    [InlineData(UnitState.Revising)]
    [InlineData(UnitState.Failed)]
    public void Every_skeleton_state_short_of_done_is_not_approved(UnitState skeleton)
    {
        Assert.NotNull(SkeletonGate.EarlyStart(Building(skeleton), "api", "feat/second"));
    }

    [Theory]
    [InlineData(UnitState.Done)]
    [InlineData(UnitState.Skipped)]
    public void A_settled_skeleton_counts_as_approved(UnitState skeleton)
    {
        Assert.Null(SkeletonGate.EarlyStart(Building(skeleton), "api", "feat/second"));
    }

    [Theory]
    [InlineData(StageState.Done)]
    [InlineData(StageState.Skipped)]
    public void A_settled_build_stage_counts_as_approved(StageState build)
    {
        var state = Building() with { Stages = [new StagePlan(Stage.Build, true, build)] };

        Assert.Null(SkeletonGate.EarlyStart(state, "api", "feat/second"));
    }

    [Fact]
    public void The_skeleton_itself_gets_no_notice()
    {
        Assert.Null(SkeletonGate.EarlyStart(Building(), "api", "feat/skeleton"));
    }

    [Fact]
    public void No_state_no_units_or_no_skeleton_unit_stays_silent()
    {
        Assert.Null(SkeletonGate.EarlyStart(null, "api", "feat/second"));
        Assert.Null(SkeletonGate.EarlyStart(State(StageState.Active), "api", "feat/second"));
        Assert.Null(SkeletonGate.EarlyStart(
            State(StageState.Active, Unit("u1", "feat/one", UnitState.Building), Unit("u2", "feat/second", UnitState.Blocked)),
            "api",
            "feat/second"));
    }

    [Fact]
    public void A_branch_that_is_no_unit_stays_silent()
    {
        Assert.Null(SkeletonGate.EarlyStart(Building(), "api", "feat/other"));
        Assert.Null(SkeletonGate.EarlyStart(Building(), "web", "feat/second"));
    }

    [Fact]
    public void Repository_and_branch_match_without_case_or_padding()
    {
        Assert.NotNull(SkeletonGate.EarlyStart(Building(), " API ", "Feat/Second"));
    }

    [Fact]
    public void Several_skeletons_all_have_to_be_settled()
    {
        var state = State(
            StageState.Active,
            Unit("u1", "feat/s1", UnitState.Done, skeleton: true),
            Unit("u2", "feat/s2", UnitState.Verifying, skeleton: true),
            Unit("u3", "feat/third", UnitState.Blocked));

        var line = SkeletonGate.EarlyStart(state, "api", "feat/third");

        Assert.NotNull(line);
        Assert.Contains("skeleton u2 is verifying", line);
    }

    [Fact]
    public void Units_with_missing_fields_from_a_hand_written_state_file_stay_silent_and_do_not_throw()
    {
        var broken = new UnitEntry(new WorkUnit(null!, null!, null!, null!, [], [], [], null!, true), UnitState.Building);
        var state = State(StageState.Active, broken, new UnitEntry(null!, UnitState.Blocked));

        Assert.Null(SkeletonGate.EarlyStart(state, "api", "feat/second"));
    }
}
