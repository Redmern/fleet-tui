using Fleet.Ui.Constants;
using Fleet.Ui.Enums;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Fleet.Ui;

public static class FleetDialog
{
    public static DialogChoice Choose(
        IApplication app,
        string title,
        IReadOnlyList<string> lines,
        string primaryText,
        string secondaryText)
    {
        var choice = DialogChoice.Cancelled;
        var window = Sized(title, lines, extraRows: 6);

        void Pick(DialogChoice picked)
        {
            choice = picked;
            app.RequestStop(window);
        }

        Answer(
            app,
            window,
            lines,
            [
                new(FleetIcons.Select, primaryText, string.Empty, () => Pick(DialogChoice.Primary)),
                new(FleetIcons.Close, secondaryText, string.Empty, () => Pick(DialogChoice.Secondary)),
            ],
            "enter");

        return choice;
    }

    public static bool Confirm(
        IApplication app, string title, IReadOnlyList<string> lines, string confirmText = "Yes")
    {
        var confirmed = false;
        var window = Sized(title, lines, extraRows: 6);

        Answer(
            app,
            window,
            lines,
            [
                new(FleetIcons.Select, confirmText, "y", () =>
                {
                    confirmed = true;
                    app.RequestStop(window);
                }),
                new(FleetIcons.Close, "No", "n", () => app.RequestStop(window)),
            ],
            "y");

        return confirmed;
    }

    private static void Answer(
        IApplication app,
        Window window,
        IReadOnlyList<string> lines,
        IReadOnlyList<FleetPillButton> pills,
        string selectKey)
    {
        var y = 1;
        foreach (var line in lines)
        {
            window.Add(FleetTheme.Caption(2, y, line));
            y++;
        }

        var list = PillRows(y + 1, pills);

        void Activate() => pills[Math.Clamp(FleetRows.Selected(list), 0, pills.Count - 1)].Activate();

        void Back()
        {
            FleetModal.Back();
            app.RequestStop(window);
        }

        list.Accepting += (_, e) =>
        {
            Activate();
            e.Handled = true;
        };

        void Keys(object? sender, Key key)
        {
            if (key.Handled)
            {
                return;
            }

            if (pills.FirstOrDefault(p => p.Answers(key)) is { } pill)
            {
                pill.Activate();
            }
            else if (key == FleetKeys.Cancel)
            {
                app.RequestStop(window);
            }
            else if (FleetKeys.GoesBack(key))
            {
                Back();
            }
            else if (key == Key.H || key == Key.CursorLeft)
            {
                FleetRows.Select(list, 0);
            }
            else if (key == Key.L || key == Key.CursorRight)
            {
                FleetRows.Select(list, pills.Count - 1);
            }
            else
            {
                return;
            }

            key.Handled = true;
        }

        ((FleetList)list).Pressing += Keys;
        window.KeyDown += Keys;

        var bar = new FleetActionBar(Pos.AnchorEnd(1));

        bar.Show(
        [
            (selectKey, FleetIcons.Select, Activate),
            ("bksp", FleetIcons.Back, Back),
        ]);

        window.Add(list, bar.Root);
        FleetCorners.Attach(window, () => app.RequestStop(window));

        list.SetFocus();

        FleetModal.Enter();

        try
        {
            app.Run(window);
        }
        finally
        {
            FleetModal.Leave();
            window.Dispose();
        }
    }

    private static ListView PillRows(Pos y, IReadOnlyList<FleetPillButton> pills)
    {
        var list = FleetTheme.Rows(1, y, pills.Count);
        FleetRows.Fill(list, [.. pills.Select(p => p.Row())]);
        return list;
    }

    public static string? Ask(IApplication app, string title, string question, bool secret = false, string initial = "")
    {
        string? answer = null;
        var lines = Wrap(question);
        var window = Sized(title, lines, extraRows: 5);

        var y = 1;
        foreach (var line in lines)
        {
            window.Add(FleetTheme.Caption(2, y, line));
            y++;
        }

        var field = FleetTheme.Field(2, y + 1, initial);
        field.Width = Terminal.Gui.ViewBase.Dim.Fill(2);
        field.Secret = secret;

        field.Accepting += (_, e) =>
        {
            answer = field.Text;
            app.RequestStop(window);
            e.Handled = true;
        };

        window.KeyDown += (_, key) =>
        {
            if (key == FleetKeys.Cancel)
            {
                app.RequestStop(window);
                key.Handled = true;
            }
        };

        field.KeyDown += (_, key) =>
        {
            if (FleetKeys.GoesBack(key, field.Text))
            {
                FleetModal.Back();
                app.RequestStop(window);
                key.Handled = true;
            }
        };

        var bar = new FleetActionBar(Pos.AnchorEnd(1));

        bar.Show(
        [
            ("enter", FleetIcons.Select, () =>
            {
                answer = field.Text;
                app.RequestStop(window);
            }),
            ("bksp", FleetIcons.Back, () =>
            {
                FleetModal.Back();
                app.RequestStop(window);
            }),
        ]);

        window.Add(field, bar.Root);
        FleetCorners.Attach(window, () => app.RequestStop(window));
        field.SetFocus();

        FleetModal.Enter();

        try
        {
            app.Run(window);
        }
        finally
        {
            FleetModal.Leave();
            window.Dispose();
        }

        return answer;
    }

    public static void Error(IApplication app, string title, string message) =>
        Notice(app, title, Wrap(message), FleetTheme.ErrorLine);

    public static void Inform(IApplication app, string title, IReadOnlyList<string> lines) =>
        Notice(app, title, [.. lines.SelectMany(line => Wrap(line))], FleetTheme.Caption);

    public static T Wait<T>(
        IApplication app, string title, IReadOnlyList<string> lines, Func<CancellationToken, Task<T>> work)
    {
        var window = Sized(title, lines, extraRows: 3);

        var y = 1;
        foreach (var text in lines)
        {
            window.Add(FleetTheme.Caption(2, y, text));
            y++;
        }

        var cancelling = FleetTheme.Caption(2, y, string.Empty);
        window.Add(cancelling);

        using var cancel = new CancellationTokenSource();
        var running = Task.Run(() => work(cancel.Token));
        _ = running.ContinueWith(_ => app.Invoke(() => app.RequestStop(window)), TaskScheduler.Default);

        window.KeyDown += (_, key) =>
        {
            if (key == FleetKeys.Cancel && !cancel.IsCancellationRequested)
            {
                cancel.Cancel();
                cancelling.Text = "Cancelling...";
            }

            key.Handled = true;
        };

        FleetModal.Enter();

        try
        {
            app.Run(window);
        }
        finally
        {
            FleetModal.Leave();
            window.Dispose();
        }

        return running.GetAwaiter().GetResult();
    }

    private static void Notice(
        IApplication app, string title, IReadOnlyList<string> lines, Func<Pos, Pos, string, View> line)
    {
        var window = Sized(title, lines, extraRows: 5);

        var y = 1;
        foreach (var text in lines)
        {
            window.Add(line(2, y, text));
            y++;
        }

        var dismiss = PillRows(
            y + 1, [new(FleetIcons.Dismiss, "Dismiss", "Enter", () => app.RequestStop(window))]);
        dismiss.Accepting += (_, e) =>
        {
            app.RequestStop(window);
            e.Handled = true;
        };

        window.KeyDown += (_, key) =>
        {
            if (key == FleetKeys.Cancel || key == Key.Enter)
            {
                app.RequestStop(window);
                key.Handled = true;
            }
            else if (FleetKeys.GoesBack(key))
            {
                FleetModal.Back();
                app.RequestStop(window);
                key.Handled = true;
            }
        };

        var bar = new FleetActionBar(Pos.AnchorEnd(1));

        bar.Show(
        [
            ("enter", FleetIcons.Select, () => app.RequestStop(window)),
            ("bksp", FleetIcons.Back, () =>
            {
                FleetModal.Back();
                app.RequestStop(window);
            }),
        ]);

        window.Add(dismiss, bar.Root);
        FleetCorners.Attach(window, () => app.RequestStop(window));
        dismiss.SetFocus();

        FleetModal.Enter();

        try
        {
            app.Run(window);
        }
        finally
        {
            FleetModal.Leave();
            window.Dispose();
        }
    }

    private static Terminal.Gui.Views.Window Sized(
        string title, IReadOnlyList<string> lines, int extraRows)
    {
        var longest = lines.Count == 0 ? 0 : lines.Max(l => l.Length);
        var width = Math.Clamp(Math.Max(longest, title.Length) + 8, 44, 92);
        var height = lines.Count + extraRows + 2 + FleetCorners.Rows;

        return FleetTheme.Modal(title, width, height);
    }

    private static IReadOnlyList<string> Wrap(string message, int width = 80)
    {
        var lines = new List<string>();

        foreach (var paragraph in message.Split('\n'))
        {
            var remaining = paragraph.TrimEnd();

            if (remaining.Length == 0)
            {
                lines.Add(string.Empty);
                continue;
            }

            while (remaining.Length > width)
            {
                var cut = remaining.LastIndexOf(' ', Math.Min(width, remaining.Length - 1));
                if (cut <= 0)
                {
                    cut = width;
                }

                lines.Add(remaining[..cut].TrimEnd());
                remaining = remaining[cut..].TrimStart();
            }

            lines.Add(remaining);
        }

        return lines;
    }
}
