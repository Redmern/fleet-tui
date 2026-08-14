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
        };

    public static IReadOnlyList<FleetAction> Configurable { get; } =
    [
        FleetAction.OpenMenu,
        FleetAction.NewProject,
        FleetAction.RemoveProject,
        FleetAction.OpenProject,
        FleetAction.Refresh,
        FleetAction.EditKeybinds,
        FleetAction.Close,
        FleetAction.MoveDown,
        FleetAction.MoveUp,
        FleetAction.MoveFirst,
        FleetAction.MoveLast,
        FleetAction.PageDown,
        FleetAction.PageUp,
        FleetAction.PrevTab,
        FleetAction.NextTab,
        FleetAction.NewAgent,
        FleetAction.RemoveAgent,
        FleetAction.ToggleHidden,
        FleetAction.AddRepository,
        FleetAction.RemoveRepository,
        FleetAction.PullRepository,
        FleetAction.ManageRepository,
        FleetAction.QuitFleet,
        FleetAction.FocusMain,
        FleetAction.ListAgents,
        FleetAction.ViewLogs,
        FleetAction.BrowseFiles,
    ];

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
            FleetAction.BrowseFiles => "File navigator in the project root",
            _ => action.ToString(),
        };
}
