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

    public static ScreenRoute Route(ViewedFrom? viewer, FleetAction action) => viewer switch
    {
        _ when !Owns(action) => ScreenRoute.Here,
        null => ScreenRoute.Here,
        { Kind: ViewKind.Ssh } => ScreenRoute.Hint,
        _ => ScreenRoute.HandBack,
    };
}
