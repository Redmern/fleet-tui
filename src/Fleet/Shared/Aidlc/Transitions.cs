using Fleet.Shared.Aidlc.Enums;
using Fleet.Shared.Results;

namespace Fleet.Shared.Aidlc;

public static class Transitions
{
    public static IReadOnlyList<StageState> Next(StageState from) => from switch
    {
        StageState.Pending => [StageState.Active, StageState.Skipped],
        StageState.Active => [StageState.Awaiting, StageState.Done],
        StageState.Awaiting => [StageState.Done, StageState.Revising],
        StageState.Revising => [StageState.Awaiting],
        _ => [],
    };

    public static IReadOnlyList<UnitState> Next(UnitState from) => from switch
    {
        UnitState.Blocked => [UnitState.Ready, UnitState.Skipped],
        UnitState.Ready => [UnitState.Building, UnitState.Skipped],
        UnitState.Building => [UnitState.Verifying, UnitState.Failed],
        UnitState.Verifying => [UnitState.Reviewing, UnitState.Building, UnitState.Failed],
        UnitState.Reviewing => [UnitState.Done, UnitState.Revising, UnitState.Failed],
        UnitState.Revising => [UnitState.Verifying, UnitState.Failed],
        UnitState.Failed => [UnitState.Building, UnitState.Skipped],
        _ => [],
    };

    public static Result<StageState> MoveStage(StageState from, StageState to, bool humanGate)
    {
        if (!Next(from).Contains(to))
        {
            return Result<StageState>.Fail(
                $"a stage cannot go from {Words.Of(from)} to {Words.Of(to)}");
        }

        if (humanGate && from == StageState.Active && to == StageState.Done)
        {
            return Result<StageState>.Fail("this stage has a human gate: it must wait for approval first");
        }

        if (!humanGate && to == StageState.Awaiting)
        {
            return Result<StageState>.Fail("this stage has no human gate, so there is nothing to wait for");
        }

        return Result<StageState>.Ok(to);
    }

    public static Result<UnitState> MoveUnit(UnitState from, UnitState to) =>
        Next(from).Contains(to)
            ? Result<UnitState>.Ok(to)
            : Result<UnitState>.Fail($"a unit cannot go from {Words.Of(from)} to {Words.Of(to)}");
}
