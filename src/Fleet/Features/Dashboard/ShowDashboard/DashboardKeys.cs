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
        FleetAction.OpenEditor,
        FleetAction.Refresh,
        FleetAction.PrevTab,
        FleetAction.NextTab,
    ];

    private static readonly FleetAction[] SubScope =
    [
        FleetAction.NewAgent,
        FleetAction.RemoveAgent,
        FleetAction.ToggleHidden,
        FleetAction.OpenEditor,
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

    private static readonly FleetAction[] NoticeScope =
    [
        FleetAction.DismissNotice,
        FleetAction.DismissAllNotices,
        FleetAction.Refresh,
        FleetAction.PrevTab,
        FleetAction.NextTab,
    ];

    public static IReadOnlyList<FleetAction> ScopeFor(int tab) => tab switch
    {
        DashboardTabs.NotificationsTab => NoticeScope,
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

        var scope = ScopeFor(tab);
        var action = keymap.ActionFor(key, scope);

        if (action == FleetAction.None
            && scope.Contains(FleetAction.ToggleHidden)
            && OtherCaseOfHide(keymap) is { IsValid: true } other
            && key == other)
        {
            action = FleetAction.ToggleHidden;
        }

        return action == FleetAction.None
            ? DashboardKey.Ignore
            : DashboardKey.Act(action);
    }

    public static string HideHint(Keymap keymap) =>
        HideLetter(keymap) is { } letter
            ? $"{char.ToLowerInvariant(letter)}/{char.ToUpperInvariant(letter)}"
            : keymap.DisplayFor(FleetAction.ToggleHidden);

    private static Key OtherCaseOfHide(Keymap keymap) =>
        HideLetter(keymap) is { } letter
            ? new Key(char.IsAsciiLetterLower(letter) ? char.ToUpperInvariant(letter) : char.ToLowerInvariant(letter))
            : Key.Empty;

    private static char? HideLetter(Keymap keymap) =>
        keymap.TextFor(FleetAction.ToggleHidden) is [var letter] && char.IsAsciiLetter(letter) ? letter : null;
}
