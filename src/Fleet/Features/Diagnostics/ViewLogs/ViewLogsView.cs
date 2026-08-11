using Fleet.Features.Diagnostics.ViewLogs.Models;
using Fleet.Ui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Fleet.Features.Diagnostics.ViewLogs;

public static class ViewLogsView
{
    public static void Show(
        IApplication app, Keymap keymap, string project, IReadOnlyList<LogEntry> entries)
    {
        var window = FleetTheme.Overlay($"fleet log — {project}");

        var list = FleetTheme.Rows(1, 1, Dim.Fill(2));

        FleetRows.Fill(list, LogRows.For(entries));
        FleetKeys.ApplyMotions(list, keymap);

        void Open()
        {
            if (entries.Count == 0)
            {
                return;
            }

            var index = Math.Clamp(FleetRows.Selected(list), 0, entries.Count - 1);

            Detail(app, keymap, entries[index]);
        }

        list.Accepting += (_, e) =>
        {
            Open();
            e.Handled = true;
        };

        var bar = new FleetActionBar(Pos.AnchorEnd(1));

        bar.Show(
        [
            ("enter", "details", Open),
            ("esc", "close", () => app.RequestStop(window)),
        ]);

        var claim = FleetModal.Enter();

        void Keys(object? sender, Key key)
        {
            if (!FleetModal.Owns(claim))
            {
                return;
            }

            if (key == FleetKeys.Cancel || key == keymap.KeyFor(Shared.Keymap.Enums.FleetAction.Close))
            {
                app.RequestStop(window);
                key.Handled = true;
            }
        }

        app.Keyboard.KeyDown += Keys;

        window.Add(list, bar.Root);

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

    private static void Detail(IApplication app, Keymap keymap, LogEntry entry)
    {
        var window = FleetTheme.Overlay("log entry");

        var list = FleetTheme.Rows(1, 1, Dim.Fill(2));

        FleetRows.Fill(list, LogRows.Detail(entry));
        FleetKeys.ApplyMotions(list, keymap);

        var bar = new FleetActionBar(Pos.AnchorEnd(1));

        bar.Show([("esc", "back", () => app.RequestStop(window))]);

        var claim = FleetModal.Enter();

        void Keys(object? sender, Key key)
        {
            if (!FleetModal.Owns(claim))
            {
                return;
            }

            if (key == FleetKeys.Cancel
                || key == Key.Enter
                || key == keymap.KeyFor(Shared.Keymap.Enums.FleetAction.Close))
            {
                app.RequestStop(window);
                key.Handled = true;
            }
        }

        app.Keyboard.KeyDown += Keys;

        window.Add(list, bar.Root);

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
