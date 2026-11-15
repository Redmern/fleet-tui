using Fleet.Ports.Settings;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Settings.Models;

namespace Fleet.Features.Menu.ShowMenu;

public sealed class ToggleSettingHandler(
    ISettingsStore settings,
    Action<SettingsConfig> saveShared,
    Action<string, string> toast)
{
    public SettingsConfig Flip(string project, FleetAction action, SettingsConfig current)
    {
        var next = FleetMenus.Flip(action, current);
        settings.Save(project, next);
        saveShared(next);
        return next;
    }

    public string Toggle(string project, FleetAction action)
    {
        var next = Flip(project, action, settings.Load(project));
        var said = Feedback(action, next);
        toast($"fleet · {project}", said);
        return said;
    }

    public const string ThisMachine = "this machine";

    public SettingsConfig FlipShared(FleetAction action, SettingsConfig shared)
    {
        var next = FleetMenus.Flip(action, shared);
        saveShared(next);
        return next;
    }

    public string ToggleShared(FleetAction action, SettingsConfig shared)
    {
        var said = Feedback(action, FlipShared(action, shared));
        toast($"fleet · {ThisMachine}", said);
        return said;
    }

    public static string Feedback(FleetAction action, SettingsConfig settings) =>
        $"{FleetMenus.Label(action).ToLowerInvariant()}: {FleetMenus.Value(action, settings)?.Trim('[', ']')}";
}
