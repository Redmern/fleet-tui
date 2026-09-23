using Fleet.Shared.Keymap.Enums;

namespace Fleet.Shared.Keymap;

public static class KeymapDefaults
{
    public const string Prefix = "Ctrl+Enter";

    public static IReadOnlyDictionary<FleetAction, string> Bindings { get; } =
        new Dictionary<FleetAction, string>
        {
            [FleetAction.OpenMenu] = "Space",
            [FleetAction.NewProject] = "n",
            [FleetAction.RemoveProject] = "d",
            [FleetAction.OpenProject] = "l",
            [FleetAction.Refresh] = "r",
            [FleetAction.EditKeybinds] = "e",
            [FleetAction.Close] = "q",
            [FleetAction.MoveDown] = "j",
            [FleetAction.MoveUp] = "k",
            [FleetAction.MoveFirst] = "g",
            [FleetAction.MoveLast] = "G",
            [FleetAction.PageDown] = "Ctrl+D",
            [FleetAction.PageUp] = "Ctrl+U",
            [FleetAction.PrevTab] = "h",
            [FleetAction.NextTab] = "l",
            [FleetAction.NewAgent] = "n",
            [FleetAction.RemoveAgent] = "m",
            [FleetAction.ToggleHidden] = "x",
            [FleetAction.AddRepository] = "n",
            [FleetAction.RemoveRepository] = "d",
            [FleetAction.PullRepository] = "p",
            [FleetAction.ManageRepository] = "m",
            [FleetAction.QuitFleet] = "Q",
            [FleetAction.FocusMain] = "m",
            [FleetAction.ListAgents] = "l",
            [FleetAction.ViewLogs] = "L",
            [FleetAction.BrowseFiles] = "f",
            [FleetAction.EditSettings] = "P",
            [FleetAction.SwitchProject] = "s",
            [FleetAction.CleanupProject] = "c",
            [FleetAction.RebuildDashboard] = "b",
            [FleetAction.EditFleetConfig] = "E",
            [FleetAction.OpenSettings] = "S",
            [FleetAction.EditAidlcMode] = "A",
        };

    public static IReadOnlyList<FleetAction> Configurable { get; } =
        [.. KeymapGroups.All.SelectMany(g => g.Actions)];

    public static string Short(FleetAction action) =>
        action switch
        {
            FleetAction.RemoveProject => "drop project",
            FleetAction.NewAgent => "new agent",
            FleetAction.ChangeHarness => "opens",
            FleetAction.ToggleHidden => "hide",
            FleetAction.RemoveAgent => "manage",
            FleetAction.AddRepository => "add repo",
            FleetAction.RemoveRepository => "drop repo",
            FleetAction.PullRepository => "pull",
            FleetAction.ManageRepository => "manage repo",
            FleetAction.EditKeybinds => "keybinds",
            FleetAction.OpenProject => "project",
            FleetAction.Refresh => "refresh",
            FleetAction.Close => "close pane",
            FleetAction.QuitFleet => "quit fleet",
            FleetAction.FocusMain => "dashboard",
            FleetAction.ListAgents => "list agents",
            FleetAction.ViewLogs => "logs",
            FleetAction.BrowseFiles => "files",
            FleetAction.EditSettings => "permissions",
            FleetAction.SwitchProject => "switch project",
            FleetAction.CleanupProject => "clean up",
            FleetAction.RebuildDashboard => "rebuild dash",
            FleetAction.EditFleetConfig => "edit fleet config",
            FleetAction.OpenSettings => "settings",
            FleetAction.EditAidlcMode => "aidlc mode",
            _ => Describe(action).ToLowerInvariant(),
        };

    public static string Describe(FleetAction action) =>
        action switch
        {
            FleetAction.OpenMenu => "Open the fleet menu",
            FleetAction.NewProject => "New project",
            FleetAction.RemoveProject => "Remove a project from fleet",
            FleetAction.OpenProject => "Open selection",
            FleetAction.AddRepository => "Add repository",
            FleetAction.Refresh => "Refresh",
            FleetAction.EditKeybinds => "Keybinds",
            FleetAction.Close => "Close this pane",
            FleetAction.MoveDown => "Move down",
            FleetAction.MoveUp => "Move up",
            FleetAction.MoveFirst => "Jump to first",
            FleetAction.MoveLast => "Jump to last",
            FleetAction.PageDown => "Page down",
            FleetAction.PageUp => "Page up",
            FleetAction.PrevTab => "Previous tab",
            FleetAction.NextTab => "Next tab",
            FleetAction.NewAgent => "New agent",
            FleetAction.ChangeHarness => "Change what an agent opens",
            FleetAction.ToggleHidden => "Hide or show an agent in the terminal",
            FleetAction.StopAgent => "Stop an agent",
            FleetAction.RemoveAgent => "Manage an agent",
            FleetAction.RemoveRepository => "Remove a repository",
            FleetAction.PullRepository => "Pull the repository",
            FleetAction.ManageRepository => "Manage the repository",
            FleetAction.QuitFleet => "Quit fleet",
            FleetAction.FocusMain => "Go to dashboard",
            FleetAction.ListAgents => "List agents",
            FleetAction.ViewLogs => "Show log",
            FleetAction.BrowseFiles => "File navigator",
            FleetAction.EditSettings => "Permissions",
            FleetAction.SwitchProject => "Switch project",
            FleetAction.CleanupProject => "Clean up stale agents",
            FleetAction.RebuildDashboard => "Rebuild the dashboard layout",
            FleetAction.EditFleetConfig => "Edit fleet config",
            FleetAction.OpenSettings => "Settings",
            FleetAction.EditAidlcMode => "AIDLC mode",
            _ => action.ToString(),
        };
}
