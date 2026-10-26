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

    public const string Show = "";

    public const string Hide = "";

    public const string HideAll = "";

    public const string Menu = "";

    public const string Dismiss = "";

    public const string DismissAll = "";

    public const string BellOn = "";

    public const string BellOff = "";

    public const string ToastsOn = "";

    public const string ToastsOff = "";

    public const string Connect = "";

    public const string NewProject = "";

    public const string Answer = "";

    public const string Rename = "";

    public const string Forget = "";

    public const string Disconnect = "";

    public const string Ports = "";

    public const string Forward = "";

    public const string Unforward = "";

    public const string OpenBrowser = "";

    public const string NewAgent = "";

    public const string Repo = "";

    public const string Branch = "";

    public const string Base = "";

    public const string Harness = "";

    public const string Create = "";

    public const string AddRepo = "";

    public const string Clone = "";

    public const string RepoName = "";

    public const string Url = "";

    public const string NewSub = "";

    public const string Task = "";

    public const string Dispatch = "";

    public const string History = "";

    public const string Edit = "";

    public const string Save = "";

    public const string SaveAndUse = "";

    public const string Delete = "";

    private static readonly Dictionary<string, string> Names = new()
    {
        [Edit] = "edit",
        [Save] = "save",
        [SaveAndUse] = "save and use",
        [Delete] = "delete",
        [Info] = "keybinds",
        [Select] = "select",
        [Back] = "back",
        [Close] = "close",
        [NewWindow] = "new window",
        [Rebind] = "rebind",
        [Configure] = "config",
        [Show] = "show",
        [Hide] = "hide",
        [HideAll] = "hide all",
        [Menu] = "menu",
        [Dismiss] = "dismiss",
        [DismissAll] = "dismiss all",
        [BellOn] = "bell on",
        [BellOff] = "bell off",
        [ToastsOn] = "toasts on",
        [ToastsOff] = "toasts off",
        [Connect] = "connect",
        [NewProject] = "new project",
        [Answer] = "answer",
        [Rename] = "rename",
        [Forget] = "forget",
        [Disconnect] = "disconnect",
        [Ports] = "ports",
        [Forward] = "forward",
        [Unforward] = "unforward",
        [OpenBrowser] = "open",
        [NewAgent] = "new agent",
        [Repo] = "repo",
        [Branch] = "branch",
        [Base] = "base",
        [Harness] = "opens",
        [Create] = "create",
        [AddRepo] = "add repository",
        [Clone] = "clone",
        [RepoName] = "name",
        [Url] = "url",
        [NewSub] = "new sub",
        [Task] = "task",
        [Dispatch] = "dispatch",
        [History] = "past tasks",
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
        FleetAction.TodoBoard => "",
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
        FleetAction.UpdateFleet => "",
        FleetAction.ShowVersion => "",
        FleetAction.ViewLogs => "",
        FleetAction.WhatsNew => "",
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
        FleetAction.NewAgent => "",
        FleetAction.ChangeHarness => "",
        FleetAction.ToggleHidden => "",
        FleetAction.HideAllAgents => HideAll,
        FleetAction.OpenWebApp => OpenBrowser,
        FleetAction.OpenPort => OpenBrowser,
        FleetAction.StopAgent => "",
        FleetAction.RemoveAgent => "",
        FleetAction.AddRepository => "",
        FleetAction.RemoveRepository => "",
        FleetAction.Refresh => "",
        FleetAction.Close => Close,
        _ => null,
    };
}
