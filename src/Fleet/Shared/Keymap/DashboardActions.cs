using Fleet.Shared.Keymap.Enums;

namespace Fleet.Shared.Keymap;

public static class DashboardActions
{
    public static IReadOnlyList<FleetAction> Served { get; } =
    [
        FleetAction.NewAgent,
        FleetAction.ChangeHarness,
        FleetAction.ToggleHidden,
        FleetAction.RemoveAgent,
        FleetAction.AddRepository,
        FleetAction.RemoveRepository,
        FleetAction.EditKeybinds,
        FleetAction.Refresh,
        FleetAction.Close,
    ];

    public static bool IsServed(FleetAction action) => Served.Contains(action);
}
