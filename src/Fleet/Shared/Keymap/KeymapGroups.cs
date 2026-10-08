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
            FleetAction.OpenSettings,
        ]),
        ("notifications",
        [
            FleetAction.DismissNotice,
            FleetAction.DismissAllNotices,
        ]),
        ("fleet menu › settings",
        [
            FleetAction.SaveSession,
            FleetAction.Remotes,
            FleetAction.OpenFleetConfigMenu,
            FleetAction.EditKeybinds,
            FleetAction.EditShowMenuKeys,
            FleetAction.EditButtonHints,
            FleetAction.EditTheme,
            FleetAction.RebuildDashboard,
            FleetAction.CleanupProject,
            FleetAction.ViewLogs,
            FleetAction.UpdateFleet,
            FleetAction.ShowVersion,
        ]),
        ("fleet menu › settings › fleet config",
        [
            FleetAction.EditMainOrchestratorInNvim,
            FleetAction.EditSubOrchestratorsInNvim,
            FleetAction.EditNvimConfig,
            FleetAction.EditAutoClose,
            FleetAction.EditAidlcMode,
            FleetAction.EditClaudeProfile,
            FleetAction.EditFleetConfig,
            FleetAction.EditHeadModel,
            FleetAction.EditMainModel,
            FleetAction.EditSubModel,
            FleetAction.EditAgentModel,
            FleetAction.EditSettings,
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
            FleetAction.RevealMenuKeys,
            FleetAction.HoldMenuKeys,
        ]),
        ("anywhere, no prefix",
        [
            FleetAction.OpenHeadVoice,
        ]),
    ];
}
