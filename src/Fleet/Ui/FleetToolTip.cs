using System.Drawing;
using Fleet.Shared.Settings.Enums;
using Fleet.Ui.Constants;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Fleet.Ui;

public static class FleetToolTip
{
    private static ToolTipHost<View>? host;

    private static IApplication? hooked;

    public static void Show(View owner, Point screen, string text)
    {
        if (FleetButtonHints.Mode != ButtonHints.Tooltips || text.Length == 0 || owner.App is not { } app)
        {
            return;
        }

        Hook(app);

        if (host is null || host.App != app)
        {
            host = new ToolTipHost<View> { App = app };
        }

        host.SetContent(() => new Label { Text = $" {text} ", SchemeName = FleetSchemes.Chip, CanFocus = false });
        host.MakeVisible(screen);
    }

    public static string Label(string name, string key) =>
        name.Length == 0 || key.Length == 0 ? name : $"{name} ({key})";

    public static void Hide()
    {
        if (host is { Visible: true })
        {
            host.Visible = false;
        }
    }

    private static void Hook(IApplication app)
    {
        if (hooked == app)
        {
            return;
        }

        hooked = app;
        app.Keyboard.KeyDown += (_, _) => Hide();
        app.Mouse.MouseEvent += (_, mouse) =>
        {
            if (mouse.IsPressed)
            {
                Hide();
            }
        };
    }
}
