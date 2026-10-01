using Fleet.Shared.Aidlc.Enums;

namespace Fleet.Shared.Aidlc.Models;

public sealed record EffectivePlan(Profile Profile, Autonomy Autonomy, IReadOnlyList<StagePlan> Stages)
{
    public IEnumerable<StagePlan> Running => Stages.Where(s => s.Runs);

    public IEnumerable<StagePlan> Gated => Running.Where(s => s.HumanGate);

    public IEnumerable<StagePlan> Skipped => Stages.Where(s => !s.Runs);

    public bool WalkingSkeleton => For(Stage.Build) is { Runs: true, HumanGate: true };

    public StagePlan? For(Stage stage) => Stages.FirstOrDefault(s => s.Stage == stage);
}
