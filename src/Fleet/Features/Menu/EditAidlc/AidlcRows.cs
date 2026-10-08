using Fleet.Shared.Aidlc;
using Fleet.Shared.Aidlc.Enums;
using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;
using Fleet.Ui.Constants;
using Fleet.Ui.Models;

namespace Fleet.Features.Menu.EditAidlc;

public static class AidlcRows
{
    public const string ModeLabel = "Ai-DLC mode";

    public const string ProfileLabel = "Default profile";

    public const string AutonomyLabel = "Autonomy";

    private const int Fixed = 3;

    public static IReadOnlyList<AidlcPart> Parts => AidlcSettings.Parts;

    public static int Count => Fixed + Parts.Count;

    public static bool IsModeRow(int index) => index == 0;

    public static bool IsProfileRow(int index) => index == 1;

    public static bool IsAutonomyRow(int index) => index == 2;

    public static AidlcPart PartAt(int index) =>
        index >= Fixed && index < Count ? Parts[index - Fixed] : AidlcPart.None;

    public static string Title(string project) => $"{project} — Ai-DLC";

    public static string Describe(AidlcPart part) => part switch
    {
        AidlcPart.SpecGate => "Spec gate",
        AidlcPart.PlanGate => "Plan gate",
        AidlcPart.DeliverGate => "Deliver gate",
        AidlcPart.WalkingSkeleton => "Walking skeleton",
        AidlcPart.Verify => "Verify stage",
        AidlcPart.Review => "Review stage",
        AidlcPart.Learn => "Learn stage",
        _ => part.ToString(),
    };

    public static string WhenOff(AidlcPart part) => part is AidlcPart.Verify or AidlcPart.Review or AidlcPart.Learn
        ? "off: the stage is skipped"
        : "off: the stage runs, its gate is automatic";

    public static IReadOnlyList<FleetRow> For(AidlcSettings settings)
    {
        var labelWidth = new[] { ModeLabel.Length, ProfileLabel.Length, AutonomyLabel.Length }
            .Concat(Parts.Select(p => Describe(p).Length))
            .Max();

        List<FleetRow> rows =
        [
            Row(ModeLabel, Words.Of(settings.Mode), FleetTones.Key, ModeDetail(settings), labelWidth),
            Row(ProfileLabel, Words.Of(settings.DefaultProfile), FleetTones.Key, ProfileCatalog.Describe(settings.DefaultProfile), labelWidth),
            Row(AutonomyLabel, Words.Of(settings.Autonomy), FleetTones.Key, AutonomyDetail(settings.Autonomy), labelWidth),
        ];

        rows.AddRange(Parts.Select(p => settings.IsOn(p)
            ? Row(Describe(p), "on", FleetTones.Good, "—", labelWidth)
            : Row(Describe(p), "off", FleetTones.Bad, WhenOff(p), labelWidth)));

        return rows;
    }

    public static IReadOnlyList<PickerEntry> ModeEntries() =>
    [
        new("off", "sub-orchestrators never run the Ai-DLC process", "o"),
        new("on", "every dispatch runs the Ai-DLC process", "n"),
        new("manual", "only when the task starts with a profile, e.g. feature: ...", "m"),
    ];

    public static IReadOnlyList<PickerEntry> ProfileEntries() =>
        [.. ProfileCatalog.All.Select(p => new PickerEntry(Words.Of(p), ProfileCatalog.Describe(p), ProfileKey(p)))];

    public static IReadOnlyList<PickerEntry> AutonomyEntries() =>
    [
        new("guided", AutonomyDetail(Autonomy.Guided), "g"),
        new("automatic", AutonomyDetail(Autonomy.Automatic), "a"),
    ];

    private static string ModeDetail(AidlcSettings settings) => settings.Mode switch
    {
        AidlcMode.On => "every dispatch",
        AidlcMode.Manual => "only with a profile prefix",
        _ => "never",
    };

    private static string ProfileKey(Profile profile) => profile switch
    {
        Profile.Research => "s",
        _ => Words.Of(profile)[..1],
    };

    private static string AutonomyDetail(Autonomy autonomy) => autonomy == Autonomy.Automatic
        ? "go on from unit to unit; failures still stop"
        : "check in with you after each unit";

    private static FleetRow Row(string label, string value, string tone, string detail, int labelWidth) =>
        new(
            [FleetSpan.Plain(label.PadRight(labelWidth))],
            [new FleetSpan(value.PadRight(10), tone), FleetSpan.Muted(detail)]);
}
