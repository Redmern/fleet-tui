using Fleet.Shared.Keymap.Enums;

namespace Fleet.Shared.Keymap;

public static class KeymapGroups
{
    public static IReadOnlyList<(string Label, IReadOnlyList<FleetAction> Actions)> All { get; } =
    [
        ("fleet menu",
        [
            FleetAction.QuitFleet,
            FleetAction.FocusMain,
            FleetAction.SwitchProject,
            FleetAction.ListAgents,
            FleetAction.BrowseFiles,
            FleetAction.Notifications,
            FleetAction.Remotes,
            FleetAction.SaveSession,
            FleetAction.OpenSettings,
        ]),
        ("notifications",
        [
            FleetAction.DismissNotice,
            FleetAction.DismissAllNotices,
        ]),
        ("fleet menu > settings",
        [
            FleetAction.RebuildDashboard,
            FleetAction.EditSettings,
            FleetAction.EditFleetConfig,
            FleetAction.EditKeybinds,
            FleetAction.ViewLogs,
            FleetAction.CleanupProject,
            FleetAction.EditAidlcMode,
            FleetAction.EditAutoClose,
            FleetAction.EditClaudeProfile,
            FleetAction.EditMainOrchestratorInNvim,
            FleetAction.EditSubOrchestratorsInNvim,
        ]),
        ("dashboard",
        [
            FleetAction.OpenMenu,
            FleetAction.Refresh,
            FleetAction.NewAgent,
            FleetAction.RemoveAgent,
            FleetAction.ToggleHidden,
            FleetAction.OpenEditor,
            FleetAction.AddRepository,
            FleetAction.RemoveRepository,
            FleetAction.PullRepository,
            FleetAction.ManageRepository,
        ]),
        ("project picker",
        [
            FleetAction.NewProject,
            FleetAction.OpenProject,
            FleetAction.RemoveProject,
        ]),
        ("navigation",
        [
            FleetAction.MoveDown,
            FleetAction.MoveUp,
            FleetAction.MoveFirst,
            FleetAction.MoveLast,
            FleetAction.PageDown,
            FleetAction.PageUp,
            FleetAction.PrevTab,
            FleetAction.NextTab,
            FleetAction.Close,
        ]),
    ];
}
