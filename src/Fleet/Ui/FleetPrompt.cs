using Fleet.Ui.Constants;
using Fleet.Ui.Models;
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

        var window = FleetTheme.Modal(title, FleetCard.Width, 8 + FleetCorners.Rows);
        var header = Static(1, 1, 1);
        var field = FleetTheme.Field(3, 2, initial);
        field.Width = Dim.Fill(3);
        var pills = Static(1, 4, 2);

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

        FleetRows.Fill(header, [Heading(label)]);
        header.SelectedItem = null;
        FleetRows.Fill(pills, [.. Pills(() => Submit(false), () => app.RequestStop(window)).Select(p => p.Row())]);
        pills.SelectedItem = null;

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

        window.Add(header, field, pills, bar.Root);
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

    private static FleetRow Heading(string label) =>
        new([new FleetSpan(FleetIcons.Edit, FleetTones.Icon), FleetSpan.Plain(" "), new FleetSpan(label, FleetTones.Key)]);

    private static IReadOnlyList<FleetPillButton> Pills(Action submit, Action cancel) =>
        FleetCard.Pills(FleetIcons.Select, "OK", "Enter", submit, cancel);

    private static ListView Static(int x, int y, int height)
    {
        var rows = FleetTheme.Rows(x, y, height);
        rows.CanFocus = false;
        return rows;
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
