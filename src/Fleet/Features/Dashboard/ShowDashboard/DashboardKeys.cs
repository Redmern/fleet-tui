using Fleet.Features.Dashboard.ShowDashboard.Models;
using Fleet.Shared.Keymap.Enums;
using Fleet.Ui;
using Terminal.Gui.Input;

namespace Fleet.Features.Dashboard.ShowDashboard;

public static class DashboardKeys
{
    private static readonly FleetAction[] Scope =
    [
        FleetAction.Refresh,
        FleetAction.NewAgent,
        FleetAction.PrevTab,
        FleetAction.NextTab,
    ];

    public static bool OpensAView(FleetAction action) =>
        action is FleetAction.OpenMenu
            or FleetAction.NewAgent
            or FleetAction.AddRepository
            or FleetAction.EditKeybinds;

    public static DashboardKey For(Key key, Keymap keymap)
    {
        if (key == FleetKeys.Cancel)
        {
            return DashboardKey.Swallow;
        }

        if (key == Key.CursorLeft)
        {
            return DashboardKey.Act(FleetAction.PrevTab);
        }

        if (key == Key.CursorRight)
        {
            return DashboardKey.Act(FleetAction.NextTab);
        }

        var action = keymap.ActionFor(key, Scope);

        return action == FleetAction.None
            ? DashboardKey.Ignore
            : DashboardKey.Act(action);
    }
}
