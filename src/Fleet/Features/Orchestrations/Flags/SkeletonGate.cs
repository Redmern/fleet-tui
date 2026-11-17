using Fleet.Shared.Aidlc;
using Fleet.Shared.Aidlc.Enums;
using Fleet.Shared.Aidlc.Models;

namespace Fleet.Features.Orchestrations.Flags;

public static class SkeletonGate
{
    public const string StartedEarly = "unit started before the skeleton gate was approved";

    public static string? EarlyStart(IntentState? state, string repository, string branch)
    {
        if (state is null || state.Stages.Any(s => s.Stage == Stage.Build && Settled(s.State)))
        {
            return null;
        }

        var started = state.Units.FirstOrDefault(u =>
            u.Unit is not null && Same(u.Unit.Repository, repository) && Same(u.Unit.Branch, branch));

        if (started is null || started.Unit.Skeleton)
        {
            return null;
        }

        var waiting = state.Units.FirstOrDefault(u => u.Unit is { Skeleton: true } && !Settled(u.State));

        return waiting is null
            ? null
            : $"{repository.Trim()}/{branch.Trim()} (unit {started.Unit.Id?.Trim()}): "
              + $"{StartedEarly} (skeleton {waiting.Unit.Id?.Trim()} is {Words.Of(waiting.State)} in state.json). "
              + "Nothing was blocked; make sure the user approved the skeleton";
    }

    private static bool Settled(StageState state) => state is StageState.Done or StageState.Skipped;

    private static bool Settled(UnitState state) => state is UnitState.Done or UnitState.Skipped;

    private static bool Same(string? a, string? b) =>
        string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
}
