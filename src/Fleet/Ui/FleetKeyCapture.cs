using Terminal.Gui.App;
using Terminal.Gui.Input;

namespace Fleet.Ui;

public static class FleetKeyCapture
{
    public static string? Show(IApplication app, string target)
    {
        string? captured = null;

        var window = FleetTheme.Modal("Press a key", 60, 8 + FleetCorners.Rows);

        window.Add(
            FleetTheme.Caption(2, 1, $"New key for: {target}"),
            FleetTheme.Caption(2, 3, "Press the key combination now."));
        FleetCorners.Attach(window, () => app.RequestStop(window));

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

        FleetModal.Enter();
        FleetKeyHints.Capturing = true;

        try
        {
            app.Run(window);
        }
        finally
        {
            FleetKeyHints.Capturing = false;
            FleetModal.Leave();
            window.Dispose();
        }

        return captured;
    }
}
