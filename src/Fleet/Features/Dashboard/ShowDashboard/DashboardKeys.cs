using Fleet.Features.Dashboard.ShowDashboard.Models;
using Fleet.Shared.Keymap.Enums;
using Fleet.Ui;
using Terminal.Gui.Input;

namespace Fleet.Features.Dashboard.ShowDashboard;

public static class DashboardKeys
{
    private static readonly FleetAction[] AgentScope =
    [
        FleetAction.NewAgent,
        FleetAction.RemoveAgent,
        FleetAction.ToggleHidden,
        FleetAction.Refresh,
        FleetAction.PrevTab,
        FleetAction.NextTab,
    ];

    private static readonly FleetAction[] SubScope =
    [
        FleetAction.NewAgent,
        FleetAction.RemoveAgent,
        FleetAction.ToggleHidden,
        FleetAction.Refresh,
        FleetAction.PrevTab,
        FleetAction.NextTab,
    ];

    private static readonly FleetAction[] RepositoryScope =
    [
        FleetAction.AddRepository,
        FleetAction.ManageRepository,
        FleetAction.Refresh,
        FleetAction.PrevTab,
        FleetAction.NextTab,
    ];

    public static IReadOnlyList<FleetAction> ScopeFor(int tab) => tab switch
    {
        DashboardTabs.RepositoriesTab => RepositoryScope,
        DashboardTabs.SubsTab => SubScope,
        _ => AgentScope,
    };

    public static bool OpensAView(FleetAction action) =>
        action is FleetAction.OpenMenu
            or FleetAction.NewAgent
            or FleetAction.ChangeHarness
            or FleetAction.RemoveAgent
            or FleetAction.AddRepository
            or FleetAction.RemoveRepository
            or FleetAction.ManageRepository
            or FleetAction.ViewLogs
            or FleetAction.EditKeybinds
            or FleetAction.EditSettings;

    public static DashboardKey For(Key key, Keymap keymap, int tab)
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

        var action = keymap.ActionFor(key, ScopeFor(tab));

        return action == FleetAction.None
            ? DashboardKey.Ignore
            : DashboardKey.Act(action);
    }
}
