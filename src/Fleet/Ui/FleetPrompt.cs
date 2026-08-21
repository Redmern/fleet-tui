using Fleet.Ui.Constants;
using Terminal.Gui.App;
using Terminal.Gui.Input;

namespace Fleet.Ui;

public static class FleetPrompt
{
    public static string? Text(
        IApplication app,
        string title,
        string initial = "",
        string label = "New name",
        bool allowEmpty = false)
    {
        string? result = null;

        var window = FleetTheme.Modal(title, 70, 8);
        var field = FleetTheme.Field(2, 3, initial);

        void Submit()
        {
            var value = field.Text.Trim();

            if (!allowEmpty && value.Length == 0)
            {
                return;
            }

            result = value;
            app.RequestStop(window);
        }

        field.Accepting += (_, e) =>
        {
            e.Handled = true;
            Submit();
        };

        window.Add(
            FleetTheme.Caption(2, 1, label),
            field,
            FleetTheme.HintBar(FleetHints.Form));

        window.KeyDown += (_, key) =>
        {
            if (key == FleetKeys.Cancel)
            {
                key.Handled = true;
                app.RequestStop(window);
            }
        };

        FleetModal.Enter();

        try
        {
            field.SetFocus();
            app.Run(window);
        }
        finally
        {
            FleetModal.Leave();
            window.Dispose();
        }

        return result;
    }
}
