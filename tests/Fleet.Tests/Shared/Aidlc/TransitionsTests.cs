using Fleet.Shared.Aidlc;
using Fleet.Shared.Aidlc.Enums;

namespace Fleet.Tests.Shared.Aidlc;

public class TransitionsTests
{
    private static readonly HashSet<(StageState, StageState)> StageMoves =
    [
        (StageState.Pending, StageState.Active),
        (StageState.Pending, StageState.Skipped),
        (StageState.Active, StageState.Awaiting),
        (StageState.Active, StageState.Done),
        (StageState.Awaiting, StageState.Done),
        (StageState.Awaiting, StageState.Revising),
        (StageState.Revising, StageState.Awaiting),
    ];

    private static readonly HashSet<(UnitState, UnitState)> UnitMoves =
    [
        (UnitState.Blocked, UnitState.Ready),
        (UnitState.Blocked, UnitState.Skipped),
        (UnitState.Ready, UnitState.Building),
        (UnitState.Ready, UnitState.Skipped),
        (UnitState.Building, UnitState.Verifying),
        (UnitState.Building, UnitState.Failed),
        (UnitState.Verifying, UnitState.Reviewing),
        (UnitState.Verifying, UnitState.Building),
        (UnitState.Verifying, UnitState.Failed),
        (UnitState.Reviewing, UnitState.Done),
        (UnitState.Reviewing, UnitState.Revising),
        (UnitState.Reviewing, UnitState.Failed),
        (UnitState.Revising, UnitState.Verifying),
        (UnitState.Revising, UnitState.Failed),
        (UnitState.Failed, UnitState.Building),
        (UnitState.Failed, UnitState.Skipped),
    ];

    [Fact]
    public void Exactly_the_listed_stage_moves_are_allowed()
    {
        foreach (var from in Enum.GetValues<StageState>())
        {
            foreach (var to in Enum.GetValues<StageState>())
            {
                Assert.Equal(StageMoves.Contains((from, to)), Transitions.Next(from).Contains(to));
            }
        }
    }

    [Fact]
    public void Exactly_the_listed_unit_moves_are_allowed_and_the_rest_fail_with_a_reason()
    {
        foreach (var from in Enum.GetValues<UnitState>())
        {
            foreach (var to in Enum.GetValues<UnitState>())
            {
                var moved = Transitions.MoveUnit(from, to);

                Assert.Equal(UnitMoves.Contains((from, to)), moved.Succeeded);

                if (!moved.Succeeded)
                {
                    Assert.Equal($"a unit cannot go from {Words.Of(from)} to {Words.Of(to)}", moved.Error);
                }
            }
        }
    }

    [Fact]
    public void Done_and_skipped_are_final()
    {
        Assert.Empty(Transitions.Next(StageState.Done));
        Assert.Empty(Transitions.Next(StageState.Skipped));
        Assert.Empty(Transitions.Next(UnitState.Done));
        Assert.Empty(Transitions.Next(UnitState.Skipped));
    }

    [Fact]
    public void A_gated_stage_cannot_jump_from_active_to_done()
    {
        var moved = Transitions.MoveStage(StageState.Active, StageState.Done, humanGate: true);

        Assert.False(moved.Succeeded);
        Assert.Contains("human gate", moved.Error);
    }

    [Fact]
    public void A_gated_stage_goes_through_awaiting_and_back_from_revising()
    {
        Assert.True(Transitions.MoveStage(StageState.Active, StageState.Awaiting, humanGate: true).Succeeded);
        Assert.True(Transitions.MoveStage(StageState.Awaiting, StageState.Revising, humanGate: true).Succeeded);
        Assert.True(Transitions.MoveStage(StageState.Revising, StageState.Awaiting, humanGate: true).Succeeded);
        Assert.True(Transitions.MoveStage(StageState.Awaiting, StageState.Done, humanGate: true).Succeeded);
    }

    [Fact]
    public void A_stage_without_a_gate_finishes_directly_and_never_awaits()
    {
        Assert.True(Transitions.MoveStage(StageState.Active, StageState.Done, humanGate: false).Succeeded);
        Assert.False(Transitions.MoveStage(StageState.Active, StageState.Awaiting, humanGate: false).Succeeded);
    }

    [Fact]
    public void An_illegal_stage_move_fails_with_both_states_named()
    {
        var moved = Transitions.MoveStage(StageState.Pending, StageState.Done, humanGate: false);

        Assert.False(moved.Succeeded);
        Assert.Equal("a stage cannot go from pending to done", moved.Error);
    }

    [Fact]
    public void Every_allowed_stage_move_succeeds_when_its_gate_rule_fits()
    {
        foreach (var (from, to) in StageMoves)
        {
            var gated = to is StageState.Awaiting || from is StageState.Awaiting or StageState.Revising;

            Assert.True(Transitions.MoveStage(from, to, gated).Succeeded, $"{from} -> {to}");
        }
    }
}
