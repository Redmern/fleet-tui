using Fleet.Shared.Aidlc.Enums;
using Fleet.Shared.Aidlc.Models;

namespace Fleet.Shared.Aidlc;

public static class ProcessPlan
{
    public const string OffInSettings = "off in settings";

    public static EffectivePlan Resolve(Profile profile, Autonomy autonomy, AidlcPart off)
    {
        var stages = ProfileCatalog.StagesOf(profile)
            .Select(stage => SwitchedOff(stage, off)
                ? new StagePlan(stage, HumanGate: false, StageState.Skipped, OffInSettings)
                : new StagePlan(
                    stage,
                    ProfileCatalog.IsGated(profile, stage) && GateOn(stage, off),
                    StageState.Pending))
            .ToList();

        return new EffectivePlan(profile, autonomy, stages);
    }

    public static AidlcPart SkipPart(Stage stage) => stage switch
    {
        Stage.Verify => AidlcPart.Verify,
        Stage.Review => AidlcPart.Review,
        Stage.Learn => AidlcPart.Learn,
        _ => AidlcPart.None,
    };

    public static AidlcPart GatePart(Stage stage) => stage switch
    {
        Stage.Specify => AidlcPart.SpecGate,
        Stage.Plan => AidlcPart.PlanGate,
        Stage.Build => AidlcPart.WalkingSkeleton,
        Stage.Deliver => AidlcPart.DeliverGate,
        _ => AidlcPart.None,
    };

    private static bool SwitchedOff(Stage stage, AidlcPart off) =>
        SkipPart(stage) is var part && part != AidlcPart.None && off.HasFlag(part);

    private static bool GateOn(Stage stage, AidlcPart off) =>
        GatePart(stage) is var part && (part == AidlcPart.None || !off.HasFlag(part));
}
