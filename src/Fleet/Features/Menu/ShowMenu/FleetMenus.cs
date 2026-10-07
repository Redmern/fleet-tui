using System.Globalization;
using Fleet.Features.Menu.ShowMenu.Models;
using Fleet.Shared.Aidlc;
using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;
using Fleet.Ui.Constants;

namespace Fleet.Features.Menu.ShowMenu;

public static class FleetMenus
{
    public static IReadOnlyList<MenuSection> Main { get; } =
    [
        new(null,
        [
            FleetAction.QuitFleet,
            FleetAction.FocusMain,
            FleetAction.SwitchProject,
            FleetAction.ListAgents,
            FleetAction.OpenEditor,
            FleetAction.BrowseFiles,
            FleetAction.Notifications,
            FleetAction.OpenSettings,
        ]),
    ];

    public static IReadOnlyList<MenuSection> Settings { get; } =
    [
        new("session", [FleetAction.SaveSession, FleetAction.Remotes], FleetIcons.Session),
        new("configure", [FleetAction.OpenFleetConfigMenu, FleetAction.EditKeybinds, FleetAction.EditShowMenuKeys], FleetIcons.Configure),
        new("maintenance", [FleetAction.RebuildDashboard, FleetAction.CleanupProject, FleetAction.ViewLogs], FleetIcons.Maintenance),
    ];

    public static IReadOnlyList<MenuSection> FleetConfig { get; } =
    [
        new(null,
        [
            FleetAction.EditMainOrchestratorInNvim,
            FleetAction.EditSubOrchestratorsInNvim,
            FleetAction.EditNvimConfig,
            FleetAction.EditAutoClose,
            FleetAction.EditAidlcMode,
            FleetAction.EditClaudeProfile,
            FleetAction.EditFleetConfig,
        ]),
        new("models", ModelRows.Actions, FleetIcons.Models),
        new("permissions", [FleetAction.EditSettings], FleetIcons.Permissions),
    ];

    private static readonly (FleetAction Submenu, IReadOnlyList<MenuSection> Sections)[] Tree =
    [
        (FleetAction.OpenSettings, Settings),
        (FleetAction.OpenFleetConfigMenu, FleetConfig),
    ];

    private static readonly FleetAction[] Deeper =
    [
        FleetAction.OpenSettings,
        FleetAction.OpenFleetConfigMenu,
        FleetAction.EditKeybinds,
        FleetAction.ViewLogs,
        FleetAction.EditAidlcMode,
        FleetAction.EditClaudeProfile,
        FleetAction.EditSettings,
    ];

    private static readonly FleetAction[] Toggles =
    [
        FleetAction.EditMainOrchestratorInNvim,
        FleetAction.EditSubOrchestratorsInNvim,
        FleetAction.EditNvimConfig,
        FleetAction.EditShowMenuKeys,
    ];

    public static bool IsSubmenu(FleetAction action) => Tree.Any(n => n.Submenu == action);

    public static IReadOnlyList<MenuSection> For(FleetAction submenu) =>
        Tree.FirstOrDefault(n => n.Submenu == submenu).Sections ?? Main;

    public static string Title(FleetAction submenu) => submenu switch
    {
        FleetAction.OpenSettings => "fleet menu › settings",
        FleetAction.OpenFleetConfigMenu => "fleet menu › settings › fleet config",
        _ => "fleet menu",
    };

    public static FleetAction Parent(FleetAction action) =>
        Tree.FirstOrDefault(n => Actions(n.Sections).Contains(action)).Submenu;

    public static IReadOnlyList<FleetAction> Actions(IReadOnlyList<MenuSection> sections) =>
        [.. sections.SelectMany(s => s.Actions)];

    public static IReadOnlyList<MenuSection> Without(IReadOnlyList<MenuSection> sections, FleetAction action) =>
        [.. sections.Select(s => s with { Actions = [.. s.Actions.Where(a => a != action)] })];

    public static bool OpensMore(FleetAction action) => Deeper.Contains(action);

    public static bool IsToggle(FleetAction action) => Toggles.Contains(action);

    public static string Label(FleetAction action) => action switch
    {
        FleetAction.QuitFleet => "Quit",
        FleetAction.FocusMain => "Dashboard",
        FleetAction.SwitchProject => "Switch",
        FleetAction.BrowseFiles => "Files",
        FleetAction.SaveSession => "Save session",
        FleetAction.RebuildDashboard => "Rebuild dashboard",
        FleetAction.CleanupProject => "Clean up agents",
        FleetAction.EditShowMenuKeys => "Show keybinds",
        _ => KeymapDefaults.Describe(action),
    };

    public static string? Value(FleetAction action, SettingsConfig settings, RoleModel? head = null) => action switch
    {
        _ when ModelRows.IsModelRow(action) =>
            ModelRows.Value(ModelRows.Current(action, settings, head ?? SettingsDefaults.HeadModel)),
        FleetAction.EditMainOrchestratorInNvim => OnOff(settings.MainOrchestratorInNvim),
        FleetAction.EditSubOrchestratorsInNvim => OnOff(settings.SubOrchestratorsInNvim),
        FleetAction.EditShowMenuKeys => OnOff(settings.ShowMenuKeys),
        FleetAction.EditNvimConfig => $"[{Words.Of(settings.Nvim)}]",
        FleetAction.EditAutoClose => settings.AutoClose
            ? $"[{settings.AutoCloseMinutes.ToString(CultureInfo.InvariantCulture)}m]"
            : OnOff(false),
        _ => null,
    };

    public static SettingsConfig Flip(FleetAction action, SettingsConfig settings) => action switch
    {
        FleetAction.EditMainOrchestratorInNvim => settings.WithMainOrchestratorInNvim(!settings.MainOrchestratorInNvim),
        FleetAction.EditSubOrchestratorsInNvim => settings.WithSubOrchestratorsInNvim(!settings.SubOrchestratorsInNvim),
        FleetAction.EditShowMenuKeys => settings.WithShowMenuKeys(!settings.ShowMenuKeys),
        FleetAction.EditNvimConfig => settings.WithNvim(
            settings.Nvim == NvimConfig.Fleet ? NvimConfig.User : NvimConfig.Fleet),
        _ => settings,
    };

    private static string OnOff(bool on) => on ? "[on]" : "[off]";
}
