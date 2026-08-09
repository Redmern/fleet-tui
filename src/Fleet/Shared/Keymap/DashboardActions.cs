using Fleet.Shared.Keymap.Enums;

namespace Fleet.Shared.Keymap;

public static class DashboardActions
{
    public static IReadOnlyList<FleetAction> Served { get; } =
    [
        FleetAction.NewAgent,
        FleetAction.AddRepository,
        FleetAction.EditKeybinds,
        FleetAction.Refresh,
    ];

    public static bool IsServed(FleetAction action) => Served.Contains(action);
}
