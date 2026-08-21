using Fleet.Ui.Models;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Fleet.Ui;

public static class FleetTextView
{
    public static void Show(
        IApplication app, string title, IReadOnlyList<string> lines, Keymap keymap)
    {
        var window = FleetTheme.Overlay(title);
        var list = FleetTheme.Rows(1, 1, Dim.Fill(2));

        FleetRows.Fill(list, [.. lines.Select(FleetRow.Plain)]);
        FleetKeys.ApplyMotions(list, keymap);

        var claim = FleetModal.Enter();

        void Keys(object? sender, Key key)
        {
            if (!FleetModal.Owns(claim))
            {
                return;
            }

            if (key == FleetKeys.Cancel || key == new Key("q"))
            {
                app.RequestStop(window);
                key.Handled = true;
            }
        }

        app.Keyboard.KeyDown += Keys;

        window.Add(list, FleetTheme.HintBar("q/esc close"));

        try
        {
            app.Run(window);
        }
        finally
        {
            FleetModal.Leave();
            app.Keyboard.KeyDown -= Keys;
            window.Dispose();
        }
    }
}
