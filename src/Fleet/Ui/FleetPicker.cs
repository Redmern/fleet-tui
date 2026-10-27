using Fleet.Shared.Keymap.Enums;
using Fleet.Ui.Constants;
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

    public static PickerEntry? Choose(
        IApplication app,
        string title,
        IReadOnlyList<PickerEntry> entries,
        Keymap keymap,
        FleetPickerOptions options,
        int selected = 0) =>
        ChooseCore(app, title, entries, keymap, selected, captureWindow: false, options)?.Entry;

    public const string NoMatches = "no matches";

    private const string FilterIcon = "\uf002";

    private const int MostRows = 20;

    private static (int Index, bool NewWindow)? ChooseCore(
        IApplication app,
        string title,
        IReadOnlyList<PickerEntry> entries,
        Keymap keymap,
        int selected,
        bool captureWindow) =>
        ChooseCore(app, title, entries, keymap, selected, captureWindow, new FleetPickerOptions()) is { } taken
            ? (taken.Index, taken.NewWindow)
            : null;

    private static (int Index, bool NewWindow, PickerEntry Entry)? ChooseCore(
        IApplication app,
        string title,
        IReadOnlyList<PickerEntry> entries,
        Keymap keymap,
        int selected,
        bool captureWindow,
        FleetPickerOptions options)
    {
        if (entries.Count == 0)
        {
            return null;
        }

        (int Index, bool NewWindow, PickerEntry Entry)? result = null;

        var filtering = options.Filter;
        var all = entries;
        var text = string.Empty;
        IReadOnlyList<int> visible = PickerFilter.Apply(all, text);
        var keys = Accelerators(all, keymap, filtering);

        var widest = entries.Max(e => e.Label.Length + e.Detail.Length + e.Key.Length) + 16;
        var extra = filtering ? 1 : 0;
        var status = options.Refresh is null ? 0 : 1;
        var cardWidth = Math.Max(widest, Math.Max(title.Length + 10, 56));
        var window = FleetTheme.Modal(
            title,
            cardWidth,
            Math.Min(entries.Count, MostRows) + 6 + extra + status + FleetCorners.Rows);

        var filterLine = FleetTheme.Caption(1, 1, string.Empty);
        var list = FleetTheme.Rows(1, 1 + extra, Dim.Fill(2 + status));
        var note = FleetTheme.StatusLine(Pos.AnchorEnd(2));
        var noteWidth = cardWidth - 4;

        int Current() =>
            visible.Count == 0 ? -1 : visible[Math.Clamp(FleetRows.Selected(list), 0, visible.Count - 1)];

        void Show(int keep)
        {
            visible = PickerFilter.Apply(all, text);

            IReadOnlyList<FleetRow> rows = visible.Count == 0
                ? [new FleetRow([FleetSpan.Muted(NoMatches)])]
                : PickerKeys.Rows([.. visible.Select(i => all[i])], [.. visible.Select(i => keys[i])]);

            FleetRows.Fill(list, rows, Math.Max(PickerFilter.Keep(visible, keep), 0));
            filterLine.Text = $"{FilterIcon} {text}";
            list.SetNeedsDraw();
        }

        Show(Math.Clamp(selected, 0, entries.Count - 1));

        if (filtering)
        {
            list.KeyBindings.ReplaceCommands(Key.N.WithCtrl, Command.Down);
            list.KeyBindings.ReplaceCommands(Key.P.WithCtrl, Command.Up);
        }
        else
        {
            FleetKeys.ApplyMotions(list, keymap);
        }

        void Take(int index, bool newWindow)
        {
            if (visible.Count == 0 || index < 0)
            {
                return;
            }

            result = (index, newWindow, all[index]);
            app.RequestStop(window);
        }

        list.Accepting += (_, e) =>
        {
            Take(Current(), newWindow: false);
            e.Handled = true;
        };

        void Back()
        {
            FleetModal.Back();
            app.RequestStop(window);
        }

        var bar = new FleetActionBar(Pos.AnchorEnd(1));

        var items = new List<(string, string, Action)>
        {
            ("enter", FleetIcons.Select, () => Take(Current(), newWindow: false)),
        };

        if (captureWindow)
        {
            items.Add(("SHIFT", FleetIcons.NewWindow, () => Take(Current(), newWindow: true)));
        }

        items.Add(("bksp", FleetIcons.Back, Back));

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
                Take(Current(), newWindow: true);
                key.Handled = true;
                return;
            }

            if (key == FleetKeys.Cancel)
            {
                app.RequestStop(window);
                key.Handled = true;
                return;
            }

            if (FleetKeys.GoesBack(key, text))
            {
                Back();
                key.Handled = true;
                return;
            }

            if (filtering)
            {
                if (PickerFilter.Edit(text, key) is { } typed)
                {
                    text = typed;
                    Show(-1);
                    key.Handled = true;
                }

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

        var open = true;
        var refreshing = false;
        object? spin = null;
        var closing = new CancellationTokenSource();

        if (options.Refresh is { } refresh)
        {
            var tick = 0;
            var token = closing.Token;

            refreshing = true;
            note.Text = $"{FleetGlyphs.Frame(tick)} {options.Refreshing}";
            spin = app.AddTimeout(TimeSpan.FromMilliseconds(90), () =>
            {
                if (!open || !refreshing)
                {
                    return false;
                }

                note.Text = $"{FleetGlyphs.Frame(++tick)} {options.Refreshing}";

                return true;
            });

            _ = Task.Run(
                async () =>
                {
                    PickerRefresh found;

                    try
                    {
                        found = await refresh(token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (Exception e)
                    {
                        found = new PickerRefresh([], $"offline: {e.Message}");
                    }

                    app.Invoke(() =>
                    {
                        if (!open)
                        {
                            return;
                        }

                        refreshing = false;

                        var keep = Current();
                        all = PickerFilter.Merge(all, found.Entries);
                        keys = Accelerators(all, keymap, filtering);
                        Show(keep);
                        note.Text = PickerFilter.OneLine(found.Note, noteWidth);
                    });
                },
                token);
        }

        window.Add(list, bar.Root);

        if (filtering)
        {
            window.Add(filterLine);
        }

        if (options.Refresh is not null)
        {
            window.Add(note);
        }

        FleetCorners.Attach(window, () => app.RequestStop(window));

        try
        {
            app.Run(window);
        }
        finally
        {
            open = false;
            closing.Cancel();

            if (spin is not null)
            {
                app.RemoveTimeout(spin);
            }

            FleetModal.Leave();
            app.Keyboard.KeyDown -= Keys;
            window.Dispose();
        }

        return result;
    }

    private static IReadOnlyList<string> Accelerators(
        IReadOnlyList<PickerEntry> entries, Keymap keymap, bool filtering) =>
        filtering ? [.. entries.Select(_ => string.Empty)] : PickerKeys.For(entries, Motions(keymap));

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
