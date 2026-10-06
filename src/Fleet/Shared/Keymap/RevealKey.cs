using Fleet.Shared.Keymap.Enums;

namespace Fleet.Shared.Keymap;

public static class RevealKey
{
    private const string AnywhereGroup = "anywhere, no prefix";

    public static IReadOnlyList<FleetAction> Keys { get; } = [FleetAction.RevealMenuKeys, FleetAction.HoldMenuKeys];

    public static IReadOnlyList<FleetAction> Guarded { get; } =
    [
        .. KeymapGroups.All
            .Where(g => g.Label != AnywhereGroup)
            .SelectMany(g => g.Actions)
            .Where(a => !Keys.Contains(a))
            .Distinct(),
    ];

    public static IReadOnlyList<FleetAction> Rivals(FleetAction action) =>
        Keys.Contains(action) ? [.. Guarded, .. Keys.Where(k => k != action)]
        : Guarded.Contains(action) ? Keys
        : [];
}
