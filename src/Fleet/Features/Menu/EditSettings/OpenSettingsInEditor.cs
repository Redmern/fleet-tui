using Fleet.Features.Menu.EditSettings.Models;
using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;

namespace Fleet.Features.Menu.EditSettings;

public sealed class OpenSettingsInEditor(
    Func<bool> exists,
    Action<SettingsConfig> save,
    Func<Action, string?> spawn,
    Func<SettingsConfig?> reload,
    Func<SettingsConfig, string?> resync,
    Editor editor = Editor.Nvim)
{
    public string Opened =>
        editor == Editor.Vscode
            ? "Opened in VS Code; the list reloads when you close its tab."
            : "Opened in nvim; the list reloads when you quit it.";

    public string OpenLabel => editor == Editor.Vscode ? "open in vs code" : "open in nvim";

    public const string Reloaded = "Reloaded from disk.";

    public const string Unparsable = "The settings file could not be read; kept the previous settings.";

    public static string? NoResync(SettingsConfig config) => null;

    public string? Open(SettingsConfig current, Action exited)
    {
        if (!exists())
        {
            save(current);
        }

        return spawn(exited);
    }

    public SettingsReload Reload(SettingsConfig previous)
    {
        if (reload() is not { } loaded)
        {
            return new SettingsReload(previous, Unparsable);
        }

        return new SettingsReload(loaded, resync(loaded) is { Length: > 0 } trouble ? trouble : Reloaded);
    }
}
