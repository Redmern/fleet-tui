using Fleet.Ui.Models;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Fleet.Ui;

public static class FleetPicker
{
    public static int? Choose(
        IApplication app,
        string title,
        IReadOnlyList<string> items,
        Keymap keymap,
        int selected = 0) =>
        Choose(app, title, PickerEntry.Plain(items), keymap, selected);

    public static int? Choose(
        IApplication app,
        string title,
        IReadOnlyList<PickerEntry> entries,
        Keymap keymap,
        int selected = 0)
    {
        if (entries.Count == 0)
        {
            return null;
        }

        int? result = null;

        var keys = PickerKeys.For(entries);

        var window = FleetTheme.Overlay(title);

        var list = FleetTheme.Rows(1, 1, Dim.Fill(2));

        FleetRows.Fill(
            list, PickerKeys.Rows(entries, keys), Math.Clamp(selected, 0, entries.Count - 1));

        FleetKeys.ApplyMotions(list, keymap);

        void Take(int index)
        {
            result = index;
            app.RequestStop(window);
        }

        list.Accepting += (_, e) =>
        {
            Take(FleetRows.Selected(list));
            e.Handled = true;
        };

        var bar = new FleetActionBar(Pos.AnchorEnd(1));

        bar.Show(
        [
            ("enter", "select", () => Take(FleetRows.Selected(list))),
            ("esc", "cancel", () => app.RequestStop(window)),
        ]);

        var claim = FleetModal.Enter();

        void Keys(object? sender, Key key)
        {
            if (!FleetModal.Owns(claim))
            {
                return;
            }

            if (key == FleetKeys.Cancel)
            {
                app.RequestStop(window);
                key.Handled = true;
                return;
            }

            for (var i = 0; i < keys.Count; i++)
            {
                if (keys[i].Length == 1 && key == new Key(keys[i]))
                {
                    Take(i);
                    key.Handled = true;
                    return;
                }
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

        return result;
    }
}
