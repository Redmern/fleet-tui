using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Viewing;
using Fleet.Shared.Viewing.Enums;

namespace Fleet.Tests.Shared.Viewing;

public class ActionGroupsTests
{
    [Fact]
    public void The_remote_owns_the_actions_of_its_project() =>
        Assert.Equal(
            Sorted(
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
            ]),
            Sorted(ActionGroups.In(ActionGroup.Project)));

    [Fact]
    public void The_remote_owns_its_own_settings_log_version_and_update() =>
        Assert.Equal(
            Sorted(
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
            ]),
            Sorted(ActionGroups.In(ActionGroup.Machine)));

    [Fact]
    public void The_machine_you_sit_at_owns_the_fleet_level_actions() =>
        Assert.Equal(
            Sorted(
            [
                FleetAction.SwitchProject,
                FleetAction.Remotes,
                FleetAction.Notifications,
                FleetAction.SaveSession,
                FleetAction.OpenHeadVoice,
                FleetAction.EditTheme,
                FleetAction.EditKeybinds,
                FleetAction.EditShowMenuKeys,
                FleetAction.EditButtonHints,
                FleetAction.ThisMachineSettings,
            ]),
            Sorted(ActionGroups.In(ActionGroup.ThisMachine)));

    [Theory]
    [InlineData(FleetAction.QuitFleet, ActionGroup.Project)]
    [InlineData(FleetAction.ViewLogs, ActionGroup.Machine)]
    [InlineData(FleetAction.Remotes, ActionGroup.ThisMachine)]
    public void An_action_names_its_owner(FleetAction action, ActionGroup group) =>
        Assert.Equal(group, ActionGroups.Of(action));

    [Theory]
    [InlineData(FleetAction.None)]
    [InlineData(FleetAction.MoveDown)]
    [InlineData(FleetAction.Close)]
    [InlineData(FleetAction.OpenMenu)]
    [InlineData(FleetAction.RevealMenuKeys)]
    public void A_key_that_only_moves_around_a_screen_has_no_owner(FleetAction action) =>
        Assert.Null(ActionGroups.Of(action));

    [Fact]
    public void No_action_has_two_owners()
    {
        var all = Enum.GetValues<ActionGroup>().SelectMany(ActionGroups.In).ToList();

        Assert.Equal(all.Count, all.Distinct().Count());
    }

    private static List<FleetAction> Sorted(IEnumerable<FleetAction> actions) => [.. actions.Order()];
}
