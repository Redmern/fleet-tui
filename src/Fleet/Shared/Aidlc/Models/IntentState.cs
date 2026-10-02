using Fleet.Shared.Aidlc.Enums;

namespace Fleet.Shared.Aidlc.Models;

public sealed record IntentState(
    string Slug,
    Profile Profile,
    Autonomy Autonomy,
    IReadOnlyList<StagePlan> Stages,
    IReadOnlyList<UnitEntry> Units,
    string Created,
    string Updated);
