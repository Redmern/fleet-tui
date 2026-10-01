using Fleet.Shared.Aidlc.Enums;

namespace Fleet.Shared.Aidlc.Models;

public sealed record StagePlan(Stage Stage, bool HumanGate, StageState State, string Reason = "")
{
    public bool Runs => State != StageState.Skipped;
}
