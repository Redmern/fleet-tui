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

    public const string NewWindow = "";

    public const string Rebind = "";

    public const string Manage = "";

    public const string Show = "";

    public const string Hide = "";

    public const string Menu = "";

    public const string Dismiss = "";

    public const string DismissAll = "";

    public const string BellOn = "";

    public const string BellOff = "";

    public const string ToastsOn = "";

    public const string ToastsOff = "";

    public const string Connect = "";

    public const string Answer = "";

    public const string Rename = "";

    public const string Forget = "";

    public const string Disconnect = "";

    private static readonly Dictionary<string, string> Names = new()
    {
        [Info] = "keybinds",
        [Select] = "select",
        [Back] = "back",
        [Close] = "close",
        [NewWindow] = "new window",
        [Rebind] = "rebind",
        [Manage] = "manage",
        [Show] = "show",
        [Hide] = "hide",
        [Menu] = "menu",
        [Dismiss] = "dismiss",
        [DismissAll] = "dismiss all",
        [BellOn] = "bell on",
        [BellOff] = "bell off",
        [ToastsOn] = "toasts on",
        [ToastsOff] = "toasts off",
        [Connect] = "connect",
        [Answer] = "answer",
        [Rename] = "rename",
        [Forget] = "forget",
        [Disconnect] = "disconnect",
        [For(FleetAction.NewAgent)!] = "new agent",
        [For(FleetAction.AddRepository)!] = "add repo",
        [For(FleetAction.Refresh)!] = "refresh",
    };

    public static string Name(string icon) => Names.GetValueOrDefault(icon, string.Empty);

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
        FleetAction.EditButtonHints => "",
        FleetAction.EditTheme => "",
        FleetAction.RebuildDashboard => "",
        FleetAction.CleanupProject => "",
        FleetAction.ViewLogs => "",
        FleetAction.EditMainOrchestratorInNvim => "",
        FleetAction.EditSubOrchestratorsInNvim => "",
        FleetAction.EditNvimConfig => "",
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
