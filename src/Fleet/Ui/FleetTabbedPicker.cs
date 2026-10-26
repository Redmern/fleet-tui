using Fleet.Shared.Keymap.Enums;
using Fleet.Ui.Constants;
using Fleet.Ui.Models;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Fleet.Ui;

public static class FleetTabbedPicker
{
    public const string EmptyHint = "(nothing here)";

    public static (int Tab, int Index, bool NewWindow)? Choose(
        IApplication app,
        string title,
        IReadOnlyList<(string Title, IReadOnlyList<PickerEntry> Entries)> tabs,
        Keymap keymap,
        int tab = 0,
        int selected = 0) =>
        Pick(app, title, tabs, keymap, [], tab, selected) is { } pick
            ? (pick.Tab, pick.Index, pick.NewWindow)
            : null;

    public static TabbedPick? Pick(
        IApplication app,
        string title,
        IReadOnlyList<(string Title, IReadOnlyList<PickerEntry> Entries)> tabs,
        Keymap keymap,
        IReadOnlyList<TabbedAction> actions,
        int tab = 0,
        int selected = 0)
    {
        if (tabs.Count == 0)
        {
            return null;
        }

        TabbedPick? result = null;
        var reserved = Reserved(keymap);
        var keys = tabs.Select(t => PickerKeys.For(t.Entries, reserved)).ToList();

        var widest = tabs.SelectMany(t => t.Entries).Select(e => e.Label.Length + e.Detail.Length + 16).DefaultIfEmpty(0).Max();
        var tallest = tabs.Max(t => Math.Max(1, t.Entries.Count));
        var tabsWidth = tabs.Sum(t => t.Title.Length + 4);
        var window = FleetTheme.Overlay(title, Math.Max(Math.Max(widest, tabsWidth + 4), Math.Max(title.Length + 10, 56)), tallest + 9 + FleetCorners.Rows);

        var tabBar = FleetTheme.TabBar(1, 0, [.. tabs.Select(t => t.Title)]);
        var list = FleetTheme.Rows(1, Pos.Bottom(tabBar.Root), Dim.Fill(2));
        FleetKeys.ApplyMotions(list, keymap);

        var current = Math.Clamp(tab, 0, tabs.Count - 1);
        var bar = new FleetActionBar(Pos.AnchorEnd(1));

        void Show(int index, int row)
        {
            current = index;
            tabBar.Select(index);
            Hints();
            var entries = tabs[index].Entries;
            FleetRows.Fill(
                list,
                entries.Count == 0 ? [FleetRow.Plain(EmptyHint)] : PickerKeys.Rows(entries, keys[index]),
                Math.Clamp(row, 0, Math.Max(0, entries.Count - 1)));
        }

        void Take(int index, bool newWindow, TabbedAction? action = null)
        {
            if (index < 0 || index >= tabs[current].Entries.Count)
            {
                return;
            }

            result = new TabbedPick(current, index, newWindow, action);
            app.RequestStop(window);
        }

        list.Accepting += (_, e) =>
        {
            Take(FleetRows.Selected(list), newWindow: false);
            e.Handled = true;
        };

        void Hints() => bar.Show(
        [
            ("enter", FleetIcons.Select, () => Take(FleetRows.Selected(list), newWindow: false)),
            ("SHIFT", FleetIcons.NewWindow, () => Take(FleetRows.Selected(list), newWindow: true)),
            .. actions.Where(a => a.Tab == current)
                .Select(a => (a.Hint, a.Icon, (Action)(() => Take(FleetRows.Selected(list), newWindow: false, a)))),
            ("bksp", FleetIcons.Back, () =>
            {
                FleetModal.Back();
                app.RequestStop(window);
            }),
        ]);

        tabBar.Chosen += index =>
        {
            if (index != current)
            {
                Show(index, 0);
            }
        };

        var claim = FleetModal.Enter();

        void Keys(object? sender, Key key)
        {
            if (!FleetModal.Owns(claim))
            {
                return;
            }

            if (actions.FirstOrDefault(a => a.Key == key && a.Tab == current) is { } action)
            {
                Take(FleetRows.Selected(list), newWindow: false, action);
            }
            else if (key == Key.Enter.WithShift)
            {
                Take(FleetRows.Selected(list), newWindow: true);
            }
            else if (key == FleetKeys.Cancel)
            {
                app.RequestStop(window);
            }
            else if (FleetKeys.GoesBack(key))
            {
                FleetModal.Back();
                app.RequestStop(window);
            }
            else if (key == Key.CursorLeft || key == keymap.KeyFor(FleetAction.PrevTab))
            {
                Show((current - 1 + tabs.Count) % tabs.Count, 0);
            }
            else if (key == Key.CursorRight || key == keymap.KeyFor(FleetAction.NextTab))
            {
                Show((current + 1) % tabs.Count, 0);
            }
            else if (Accelerator(keys[current], key) is var (index, newWindow))
            {
                Take(index, newWindow);
            }
            else
            {
                return;
            }

            key.Handled = true;
        }

        app.Keyboard.KeyDown += Keys;
        window.Add(tabBar.Root, list, bar.Root);
        FleetCorners.Attach(window, () => app.RequestStop(window), tabBar.Root);
        Show(current, selected);

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

    private static (int Index, bool NewWindow)? Accelerator(IReadOnlyList<string> keys, Key key)
    {
        for (var i = 0; i < keys.Count; i++)
        {
            if (keys[i].Length != 1)
            {
                continue;
            }

            var accelerator = new Key(keys[i]);

            if (key == accelerator)
            {
                return (i, false);
            }

            if (key == accelerator.WithShift)
            {
                return (i, true);
            }
        }

        return null;
    }

    private static IReadOnlySet<char> Reserved(Keymap keymap)
    {
        var reserved = new HashSet<char>();

        foreach (var action in new[]
        {
            FleetAction.MoveDown,
            FleetAction.MoveUp,
            FleetAction.MoveFirst,
            FleetAction.MoveLast,
            FleetAction.PrevTab,
            FleetAction.NextTab,
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
