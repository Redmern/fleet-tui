using Fleet.Shared.Keymap.Enums;

namespace Fleet.Shared.Keymap;

public static class FleetActionIds
{
    public static string For(FleetAction action) => action switch
    {
        FleetAction.AddRepository => "add-repository",
        FleetAction.EditKeybinds => "keybinds",
        FleetAction.OpenProject => "open-project",
        FleetAction.NewProject => "new-project",
        FleetAction.Refresh => "refresh",
        FleetAction.NewAgent => "new-agent",
        _ => action.ToString().ToLowerInvariant(),
    };

    public static FleetAction Parse(string id) => id.Trim().ToLowerInvariant() switch
    {
        "add-repository" => FleetAction.AddRepository,
        "keybinds" => FleetAction.EditKeybinds,
        "open-project" => FleetAction.OpenProject,
        "new-project" => FleetAction.NewProject,
        "refresh" => FleetAction.Refresh,
        "new-agent" => FleetAction.NewAgent,
        _ => FleetAction.None,
    };
}
