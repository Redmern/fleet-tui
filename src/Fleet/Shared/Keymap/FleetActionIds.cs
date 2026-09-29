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
        FleetAction.ChangeHarness => "change-harness",
        FleetAction.ToggleHidden => "toggle-hidden",
        FleetAction.StopAgent => "stop-agent",
        FleetAction.RemoveAgent => "remove-agent",
        FleetAction.RemoveRepository => "remove-repository",
        FleetAction.PullRepository => "pull-repository",
        FleetAction.ManageRepository => "manage-repository",
        FleetAction.QuitFleet => "quit",
        FleetAction.FocusMain => "main-pane",
        FleetAction.ListAgents => "list-agents",
        FleetAction.EditSettings => "settings",
        FleetAction.SwitchProject => "switch-project",
        FleetAction.CleanupProject => "cleanup",
        FleetAction.RebuildDashboard => "rebuild-dashboard",
        FleetAction.EditFleetConfig => "edit-fleet-config",
        FleetAction.OpenSettings => "settings-menu",
        FleetAction.EditAidlcMode => "aidlc-mode",
        FleetAction.EditClaudeProfile => "claude-profile",
        FleetAction.Notifications => "notifications",
        FleetAction.Remotes => "remotes",
        FleetAction.SaveSession => "save-session",
        FleetAction.DismissNotice => "dismiss-notice",
        FleetAction.DismissAllNotices => "dismiss-all-notices",
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
        "change-harness" => FleetAction.ChangeHarness,
        "toggle-hidden" => FleetAction.ToggleHidden,
        "stop-agent" => FleetAction.StopAgent,
        "remove-agent" => FleetAction.RemoveAgent,
        "remove-repository" => FleetAction.RemoveRepository,
        "pull-repository" => FleetAction.PullRepository,
        "manage-repository" => FleetAction.ManageRepository,
        "quit" => FleetAction.QuitFleet,
        "main-pane" => FleetAction.FocusMain,
        "list-agents" => FleetAction.ListAgents,
        "settings" => FleetAction.EditSettings,
        "switch-project" => FleetAction.SwitchProject,
        "cleanup" => FleetAction.CleanupProject,
        "rebuild-dashboard" => FleetAction.RebuildDashboard,
        "edit-fleet-config" => FleetAction.EditFleetConfig,
        "settings-menu" => FleetAction.OpenSettings,
        "aidlc-mode" => FleetAction.EditAidlcMode,
        "claude-profile" => FleetAction.EditClaudeProfile,
        "notifications" => FleetAction.Notifications,
        "remotes" => FleetAction.Remotes,
        "save-session" => FleetAction.SaveSession,
        "dismiss-notice" => FleetAction.DismissNotice,
        "dismiss-all-notices" => FleetAction.DismissAllNotices,
        "close" => FleetAction.Close,
        var other => Enum.GetValues<FleetAction>().FirstOrDefault(a => For(a) == other),
    };
}
