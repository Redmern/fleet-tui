using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Viewing.Enums;

namespace Fleet.Shared.Viewing;

public static class ActionGroups
{
    private static readonly IReadOnlyDictionary<ActionGroup, IReadOnlyList<FleetAction>> Table =
        new Dictionary<ActionGroup, IReadOnlyList<FleetAction>>
        {
            [ActionGroup.Project] =
            [
                FleetAction.NewAgent,
                FleetAction.ListAgents,
                FleetAction.ChangeHarness,
                FleetAction.StopAgent,
                FleetAction.RemoveAgent,
                FleetAction.ToggleHidden,
                FleetAction.HideAllAgents,
                FleetAction.FocusMain,
                FleetAction.AddRepository,
                FleetAction.RemoveRepository,
                FleetAction.PullRepository,
                FleetAction.ManageRepository,
                FleetAction.BrowseFiles,
                FleetAction.OpenEditor,
                FleetAction.TodoBoard,
                FleetAction.EditSettings,
                FleetAction.CleanupProject,
                FleetAction.RebuildDashboard,
                FleetAction.RemoveProject,
                FleetAction.OpenWebApp,
                FleetAction.OpenPort,
                FleetAction.QuitFleet,
            ],
            [ActionGroup.Machine] =
            [
                FleetAction.OpenSettings,
                FleetAction.OpenFleetConfigMenu,
                FleetAction.EditHeadModel,
                FleetAction.EditMainModel,
                FleetAction.EditSubModel,
                FleetAction.EditAgentModel,
                FleetAction.EditDefaultHarness,
                FleetAction.EditAidlcMode,
                FleetAction.EditClaudeProfile,
                FleetAction.EditAutoClose,
                FleetAction.EditEditor,
                FleetAction.EditMainOrchestratorInNvim,
                FleetAction.EditSubOrchestratorsInNvim,
                FleetAction.EditNvimConfig,
                FleetAction.EditFleetConfig,
                FleetAction.ViewLogs,
                FleetAction.ShowVersion,
                FleetAction.WhatsNew,
                FleetAction.UpdateFleet,
            ],
            [ActionGroup.ThisMachine] =
            [
                FleetAction.SwitchProject,
                FleetAction.Notifications,
                FleetAction.Remotes,
                FleetAction.SaveSession,
                FleetAction.EditKeybinds,
                FleetAction.EditTheme,
                FleetAction.EditShowMenuKeys,
                FleetAction.EditButtonHints,
                FleetAction.ThisMachineSettings,
                FleetAction.OpenHeadVoice,
            ],
        };

    public static IReadOnlyList<FleetAction> In(ActionGroup group) => Table[group];

    public static ActionGroup? Of(FleetAction action) =>
        Table.Where(g => g.Value.Contains(action)).Select(g => (ActionGroup?)g.Key).FirstOrDefault();
}
