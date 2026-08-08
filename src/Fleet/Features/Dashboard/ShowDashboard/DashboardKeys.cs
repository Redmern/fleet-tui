using Fleet.Features.Dashboard.ShowDashboard.Enums;
using Fleet.Ui;
using Terminal.Gui.Input;

namespace Fleet.Features.Dashboard.ShowDashboard;

public static class DashboardKeys
{
    public static DashboardAction For(Key key)
    {
        if (key == FleetKeys.Quit)
        {
            return DashboardAction.Quit;
        }

        if (key == FleetKeys.Add)
        {
            return DashboardAction.Add;
        }

        if (key == FleetKeys.Refresh)
        {
            return DashboardAction.Refresh;
        }

        return DashboardAction.None;
    }
}
