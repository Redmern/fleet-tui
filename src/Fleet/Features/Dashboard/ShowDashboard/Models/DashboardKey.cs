using Fleet.Shared.Keymap.Enums;

namespace Fleet.Features.Dashboard.ShowDashboard.Models;

public sealed record DashboardKey(bool Consume, FleetAction Action)
{
    public static readonly DashboardKey Ignore = new(false, FleetAction.None);

    public static readonly DashboardKey Swallow = new(true, FleetAction.None);

    public static DashboardKey Act(FleetAction action) => new(true, action);
}
