using Fleet.Ui;
using Fleet.Ui.Constants;
using Terminal.Gui.App;
using Terminal.Gui.Input;

namespace Fleet.Features.Menu.EditKeybinds;

public static class CaptureKeyView
{
    public static string? Show(IApplication app, string target)
    {
        string? captured = null;

        var window = FleetTheme.Modal("Press a key", 60, 8);

        window.Add(
            FleetTheme.Caption(2, 1, $"New key for: {target}"),
            FleetTheme.Caption(2, 3, "Press the key combination now."),
            FleetTheme.HintBar(FleetHints.Capture));

        window.KeyDown += (_, key) =>
        {
            key.Handled = true;

            if (key == Key.Esc)
            {
                app.RequestStop(window);
                return;
            }

            if (key.IsModifierOnly || !key.IsValid)
            {
                return;
            }

            captured = key.ToString();
            app.RequestStop(window);
        };

        app.Run(window);
        window.Dispose();

        return captured;
    }
}
