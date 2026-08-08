using Fleet.Features.Dashboard.ShowDashboard.Models;
using Fleet.Shared.Keymap.Enums;
using Fleet.Ui;
using Terminal.Gui.Input;

namespace Fleet.Features.Dashboard.ShowDashboard;

public static class DashboardKeys
{
    public static DashboardKey For(Key key, Keymap keymap)
    {
        if (key == FleetKeys.Cancel)
        {
            return DashboardKey.Swallow;
        }

        var action = keymap.ActionFor(key);

        return action switch
        {
            FleetAction.Close
                or FleetAction.AddRepository
                or FleetAction.Refresh => DashboardKey.Act(action),
            _ => DashboardKey.Ignore,
        };
    }
}
