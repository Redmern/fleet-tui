using Fleet.Shared.Keymap.Enums;
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
        int selected = 0) =>
        ChooseCore(app, title, entries, keymap, selected, captureWindow: false)?.Index;

    public static (int Index, bool NewWindow)? ChooseWithWindow(
        IApplication app,
        string title,
        IReadOnlyList<string> items,
        Keymap keymap,
        int selected = 0) =>
        ChooseWithWindow(app, title, PickerEntry.Plain(items), keymap, selected);

    public static (int Index, bool NewWindow)? ChooseWithWindow(
        IApplication app,
        string title,
        IReadOnlyList<PickerEntry> entries,
        Keymap keymap,
        int selected = 0) =>
        ChooseCore(app, title, entries, keymap, selected, captureWindow: true);

    private static (int Index, bool NewWindow)? ChooseCore(
        IApplication app,
        string title,
        IReadOnlyList<PickerEntry> entries,
        Keymap keymap,
        int selected,
        bool captureWindow)
    {
        if (entries.Count == 0)
        {
            return null;
        }

        (int Index, bool NewWindow)? result = null;

        var keys = PickerKeys.For(entries, Motions(keymap));

        var window = FleetTheme.Overlay(title);

        var list = FleetTheme.Rows(1, 1, Dim.Fill(2));

        FleetRows.Fill(
            list, PickerKeys.Rows(entries, keys), Math.Clamp(selected, 0, entries.Count - 1));

        FleetKeys.ApplyMotions(list, keymap);

        void Take(int index, bool newWindow)
        {
            result = (index, newWindow);
            app.RequestStop(window);
        }

        list.Accepting += (_, e) =>
        {
            Take(FleetRows.Selected(list), newWindow: false);
            e.Handled = true;
        };

        var bar = new FleetActionBar(Pos.AnchorEnd(1));

        var items = new List<(string, string, Action)>
        {
            ("enter", "select", () => Take(FleetRows.Selected(list), newWindow: false)),
        };

        if (captureWindow)
        {
            items.Add(("SHIFT", "new window", () => Take(FleetRows.Selected(list), newWindow: true)));
        }

        items.Add(("esc", "cancel", () => app.RequestStop(window)));

        bar.Show(items);

        var claim = FleetModal.Enter();

        void Keys(object? sender, Key key)
        {
            if (!FleetModal.Owns(claim))
            {
                return;
            }

            if (captureWindow && key == Key.Enter.WithShift)
            {
                Take(FleetRows.Selected(list), newWindow: true);
                key.Handled = true;
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
                if (keys[i].Length != 1)
                {
                    continue;
                }

                var accelerator = new Key(keys[i]);

                if (key == accelerator)
                {
                    Take(i, newWindow: false);
                    key.Handled = true;
                    return;
                }

                if (captureWindow && key == accelerator.WithShift)
                {
                    Take(i, newWindow: true);
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

    private static IReadOnlySet<char> Motions(Keymap keymap)
    {
        var reserved = new HashSet<char>();

        foreach (var action in new[]
        {
            FleetAction.MoveDown,
            FleetAction.MoveUp,
            FleetAction.MoveFirst,
            FleetAction.MoveLast,
        })
        {
            var text = keymap.TextFor(action);

            if (text.Length == 1)
            {
                reserved.Add(char.ToLowerInvariant(text[0]));
            }
        }

        return reserved;
    }
}
