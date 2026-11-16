using System.Diagnostics.CodeAnalysis;
using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Viewing.Enums;
using Fleet.Shared.Viewing.Models;

namespace Fleet.Shared.Viewing;

public static class ViewerScreens
{
    public static IReadOnlyList<FleetAction> Owned { get; } =
    [
        FleetAction.SwitchProject,
        FleetAction.Notifications,
        FleetAction.Remotes,
        FleetAction.SaveSession,
        FleetAction.EditKeybinds,
        FleetAction.EditTheme,
        FleetAction.EditShowMenuKeys,
        FleetAction.EditButtonHints,
        FleetAction.ThisMachineSettings,
    ];

    public const FleetAction Head = FleetAction.OpenHeadVoice;

    private static readonly IReadOnlyList<string> FlippedWithoutAScreen =
    [
        FleetActionIds.For(FleetAction.EditShowMenuKeys),
        FleetActionIds.For(FleetAction.EditButtonHints),
    ];

    public static IReadOnlyList<string> Ids { get; } = [.. Owned.Select(FleetActionIds.For)];

    public static IReadOnlyList<string> Legacy { get; } =
    [
        FleetActionIds.For(FleetAction.SwitchProject),
        FleetActionIds.For(FleetAction.Notifications),
    ];

    public static bool Owns([NotNullWhen(true)] string? id) => id is not null && Ids.Contains(id, StringComparer.Ordinal);

    public static bool Owns(FleetAction action) => Owned.Contains(action);

    public static IReadOnlyList<string> TakenBackBy(IReadOnlyList<string>? announced) =>
        announced is { Count: > 0 } ? [.. announced.Where(Owns).Distinct(StringComparer.Ordinal)] : Legacy;

    public static string OpenedWhenHandedBack(string id) =>
        FlippedWithoutAScreen.Contains(id, StringComparer.Ordinal) ? FleetActionIds.For(FleetAction.ThisMachineSettings) : id;

    public static ScreenRoute Route(ViewedFrom? viewer, FleetAction action) => viewer switch
    {
        null => ScreenRoute.Here,
        _ when action == Head => ScreenRoute.Hint,
        _ when !Owns(action) => ScreenRoute.Here,
        { Kind: ViewKind.Ssh } => ScreenRoute.Hint,
        _ => ScreenRoute.HandBack,
    };
}
