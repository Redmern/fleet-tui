using Fleet.Shared.Keymap.Enums;

namespace Fleet.Shared.Keymap;

public static class KeymapDefaults
{
    public const string Prefix = "Ctrl+Space";

    public static IReadOnlyDictionary<FleetAction, string> Bindings { get; } =
        new Dictionary<FleetAction, string>
        {
            [FleetAction.OpenMenu] = "Space",
            [FleetAction.NewProject] = "n",
            [FleetAction.OpenProject] = "l",
            [FleetAction.Refresh] = "r",
            [FleetAction.EditKeybinds] = "k",
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
            [FleetAction.ChangeHarness] = "c",
            [FleetAction.ToggleHidden] = "x",
            [FleetAction.StopAgent] = "s",
            [FleetAction.RemoveAgent] = "d",
        };

    public static IReadOnlyList<FleetAction> Configurable { get; } =
    [
        FleetAction.OpenMenu,
        FleetAction.NewProject,
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
        FleetAction.ChangeHarness,
        FleetAction.ToggleHidden,
        FleetAction.StopAgent,
        FleetAction.RemoveAgent,
    ];

    public static string Describe(FleetAction action) =>
        action switch
        {
            FleetAction.OpenMenu => "Open the fleet menu",
            FleetAction.NewProject => "New project",
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
            FleetAction.RemoveAgent => "Remove an agent and its worktree",
            _ => action.ToString(),
        };
}
