using Fleet.Ui.Constants;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Fleet.Ui;

public static class FleetPrompt
{
    public sealed record Reply(string Value, bool SubmitsDialog);

    public static string? Text(
        IApplication app,
        string title,
        string initial = "",
        string label = "New name",
        bool allowEmpty = false) =>
        Ask(app, title, initial, label, allowEmpty)?.Value;

    public static Reply? Ask(
        IApplication app,
        string title,
        string initial = "",
        string label = "New name",
        bool allowEmpty = false)
    {
        Reply? result = null;

        var window = FleetTheme.Modal(title, 70, 8 + FleetCorners.Rows);
        var field = FleetTheme.Field(2, 3, initial);

        void Submit(bool submitsDialog)
        {
            var value = field.Text.Trim();

            if (!allowEmpty && value.Length == 0)
            {
                return;
            }

            result = new Reply(value, submitsDialog);
            app.RequestStop(window);
        }

        Submits(field, Submit);

        var bar = new FleetActionBar(Pos.AnchorEnd(1));

        bar.Show(
        [
            ("enter", FleetIcons.Select, () => Submit(false)),
            ("bksp", FleetIcons.Back, () =>
            {
                FleetModal.Back();
                app.RequestStop(window);
            }),
        ]);

        window.Add(FleetTheme.Caption(2, 1, label), field, bar.Root);
        FleetCorners.Attach(window, () => app.RequestStop(window));

        window.KeyDown += (_, key) =>
        {
            if (key == FleetKeys.Cancel)
            {
                key.Handled = true;
                app.RequestStop(window);
            }
        };

        field.KeyDown += (_, key) =>
        {
            if (FleetKeys.GoesBack(key, field.Text))
            {
                key.Handled = true;
                FleetModal.Back();
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

    public static void Submits(TextField field, Action<bool> submit)
    {
        field.Accepting += (_, e) =>
        {
            e.Handled = true;
            submit(false);
        };

        field.KeyDown += (_, key) =>
        {
            if (key == Key.Enter.WithCtrl)
            {
                key.Handled = true;
                submit(true);
            }
        };
    }
}
