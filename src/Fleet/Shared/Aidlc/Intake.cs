using Fleet.Shared.Aidlc.Enums;
using Fleet.Shared.Aidlc.Models;

namespace Fleet.Shared.Aidlc;

public static class Intake
{
    public static IntakeRecord Start(string slug, EffectivePlan plan, ProfileSource source, string stampUtc)
    {
        var stages = plan.Stages
            .Select(s => s.Stage == Stage.Intake ? s with { State = StageState.Done } : s)
            .ToList();

        var state = new IntentState(slug, plan.Profile, plan.Autonomy, stages, [], stampUtc, stampUtc);

        List<AuditEntry> events =
        [
            new(stampUtc, AuditActor.Engine, AuditEvent.IntentCreated, Detail: slug),
            new(stampUtc, AuditActor.Engine, AuditEvent.ProfileSet, Detail: $"{Words.Of(plan.Profile)} ({Describe(source)})"),
            .. plan.Skipped.Select(s => new AuditEntry(
                stampUtc, AuditActor.Engine, AuditEvent.StageSkipped, Detail: $"{Words.Of(s.Stage)}: {s.Reason}")),
        ];

        return new IntakeRecord(state, events);
    }

    public static string Describe(ProfileSource source) => source switch
    {
        ProfileSource.Prefix => "from the task prefix",
        ProfileSource.Argument => "from the dispatch argument",
        _ => "the project default",
    };
}
