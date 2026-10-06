using Fleet.Shared.Keymap.Enums;

namespace Fleet.Ui.Constants;

public static class FleetIcons
{
    public const string Session = "";

    public const string Configure = "";

    public const string Maintenance = "";

    public const string Models = "";

    public const string Permissions = "";

    public const string Info = "";

    public const string Select = "";

    public const string Back = "";

    public const string Close = "";

    public static string? For(FleetAction action) => action switch
    {
        FleetAction.QuitFleet => "",
        FleetAction.FocusMain => "",
        FleetAction.SwitchProject => "",
        FleetAction.ListAgents => "",
        FleetAction.OpenEditor => "",
        FleetAction.BrowseFiles => "",
        FleetAction.Notifications => "",
        FleetAction.OpenSettings => "",
        FleetAction.SaveSession => "",
        FleetAction.Remotes => "",
        FleetAction.OpenFleetConfigMenu => "",
        FleetAction.EditKeybinds => "",
        FleetAction.EditShowMenuKeys => "",
        FleetAction.RebuildDashboard => "",
        FleetAction.CleanupProject => "",
        FleetAction.ViewLogs => "",
        FleetAction.EditMainOrchestratorInNvim => "",
        FleetAction.EditSubOrchestratorsInNvim => "",
        FleetAction.EditAutoClose => "",
        FleetAction.EditAidlcMode => "",
        FleetAction.EditClaudeProfile => "",
        FleetAction.EditFleetConfig => "",
        FleetAction.EditSettings => "",
        FleetAction.EditHeadModel => "",
        FleetAction.EditMainModel => "",
        FleetAction.EditSubModel => "",
        FleetAction.EditAgentModel => "",
        FleetAction.NewProject => "",
        FleetAction.OpenProject => "",
        FleetAction.RemoveProject => "",
        FleetAction.NewAgent => "",
        FleetAction.ChangeHarness => "",
        FleetAction.ToggleHidden => "",
        FleetAction.StopAgent => "",
        FleetAction.RemoveAgent => "",
        FleetAction.AddRepository => "",
        FleetAction.RemoveRepository => "",
        FleetAction.Refresh => "",
        FleetAction.Close => Close,
        _ => null,
    };
}
