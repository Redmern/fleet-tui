using Fleet.Shared.Aidlc;
using Fleet.Shared.Aidlc.Enums;
using Fleet.Shared.Aidlc.Models;
using Fleet.Shared.Settings.Enums;

namespace Fleet.Shared.Settings.Models;

public sealed record AidlcSettings(AidlcMode Mode, Profile DefaultProfile, Autonomy Autonomy, AidlcPart Off)
{
    public static AidlcSettings Default => new(AidlcMode.Off, ProfileCatalog.Default, Autonomy.Guided, AidlcPart.None);

    public static IReadOnlyList<AidlcPart> Parts { get; } =
    [
        AidlcPart.SpecGate,
        AidlcPart.PlanGate,
        AidlcPart.DeliverGate,
        AidlcPart.WalkingSkeleton,
        AidlcPart.Verify,
        AidlcPart.Review,
        AidlcPart.Learn,
    ];

    public bool IsOn(AidlcPart part) => (Off & part) == AidlcPart.None;

    public AidlcSettings With(AidlcPart part, bool on) => this with { Off = on ? Off & ~part : Off | part };

    public EffectivePlan Plan(Profile profile) => ProcessPlan.Resolve(profile, Autonomy, Off);

    public string Signature =>
        $"aidlc={Mode}:{Words.Of(DefaultProfile)}:{Words.Of(Autonomy)}:off="
        + string.Join(',', Parts.Where(p => !IsOn(p)).Select(Words.Of));
}
