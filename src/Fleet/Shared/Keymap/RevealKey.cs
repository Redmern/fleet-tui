using Fleet.Shared.Keymap.Enums;

namespace Fleet.Shared.Keymap;

public static class RevealKey
{
    private const string MenuGroups = "fleet menu";

    public static IReadOnlyList<FleetAction> Guarded { get; } =
    [
        .. KeymapGroups.All
            .Where(g => g.Label.StartsWith(MenuGroups, StringComparison.Ordinal))
            .SelectMany(g => g.Actions),
        FleetAction.OpenEditor,
        FleetAction.MoveDown,
        FleetAction.MoveUp,
        FleetAction.MoveFirst,
        FleetAction.MoveLast,
        FleetAction.PageDown,
        FleetAction.PageUp,
        FleetAction.Close,
    ];

    public static IReadOnlyList<FleetAction> Rivals(FleetAction action) =>
        action == FleetAction.RevealMenuKeys ? Guarded
        : Guarded.Contains(action) ? [FleetAction.RevealMenuKeys]
        : [];
}
