using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Enums;
using Fleet.Ui.Constants;
using Terminal.Gui.App;

namespace Fleet.Ui;

public static class FleetButtonHints
{
    private static ButtonHints mode = SettingsDefaults.ButtonHints;

    public static event Action? Changed;

    public static ButtonHints Mode => mode;

    public static void Apply(ButtonHints hints)
    {
        if (hints == mode)
        {
            return;
        }

        mode = hints;

        if (hints != ButtonHints.Tooltips)
        {
            FleetToolTip.Hide();
        }

        Changed?.Invoke();
    }

    public static void Reset()
    {
        mode = SettingsDefaults.ButtonHints;
        Changed = null;
    }

    public static void Attach(IApplication app, Func<ButtonHints> load)
    {
        Apply(load());

        app.AddTimeout(FleetKeyHints.Recheck, () =>
        {
            Apply(load());
            return true;
        });
    }

    public static string Face(string icon) => Face(icon, mode);

    public static string Face(string icon, ButtonHints hints) =>
        hints == ButtonHints.Text && FleetIcons.Name(icon) is { Length: > 0 } name ? $"{icon} {name}" : icon;
}
