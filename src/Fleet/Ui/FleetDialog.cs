using Fleet.Ui.Constants;
using Fleet.Ui.Enums;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

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
        var window = Sized(title, lines, extraRows: 5);

        var y = 1;
        foreach (var line in lines)
        {
            window.Add(FleetTheme.Caption(2, y, line));
            y++;
        }

        var primary = FleetTheme.Submit(2, y + 1, primaryText);
        var secondary = FleetTheme.Secondary(2 + primaryText.Length + 6, y + 1, secondaryText);

        primary.Accepting += (_, _) =>
        {
            choice = DialogChoice.Primary;
            app.RequestStop(window);
        };

        secondary.Accepting += (_, _) =>
        {
            choice = DialogChoice.Secondary;
            app.RequestStop(window);
        };

        window.KeyDown += (_, key) =>
        {
            if (key == FleetKeys.Cancel)
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
            else if (key == Key.H || key == Key.CursorLeft)
            {
                primary.SetFocus();
                key.Handled = true;
            }
            else if (key == Key.L || key == Key.CursorRight)
            {
                secondary.SetFocus();
                key.Handled = true;
            }
        };

        var bar = new FleetActionBar(Pos.AnchorEnd(1));

        bar.Show(
        [
            ("enter", FleetIcons.Select, () =>
            {
                choice = primary.HasFocus ? DialogChoice.Primary : DialogChoice.Secondary;
                app.RequestStop(window);
            }),
            ("bksp", FleetIcons.Back, () =>
            {
                FleetModal.Back();
                app.RequestStop(window);
            }),
        ]);

        window.Add(primary, secondary, bar.Root);
        FleetCorners.Attach(window, () => app.RequestStop(window));

        primary.SetFocus();

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

        return choice;
    }

    public static bool Confirm(
        IApplication app, string title, IReadOnlyList<string> lines, string confirmText = "Yes")
    {
        var confirmed = false;
        var window = Sized(title, lines, extraRows: 5);

        var y = 1;
        foreach (var line in lines)
        {
            window.Add(FleetTheme.Caption(2, y, line));
            y++;
        }

        var yes = FleetTheme.Submit(2, y + 1, confirmText);
        var no = FleetTheme.Secondary(2 + confirmText.Length + 6, y + 1, "No");

        yes.Accepting += (_, _) =>
        {
            confirmed = true;
            app.RequestStop(window);
        };

        no.Accepting += (_, _) => app.RequestStop(window);

        window.KeyDown += (_, key) =>
        {
            if (key == Key.Y)
            {
                confirmed = true;
                app.RequestStop(window);
                key.Handled = true;
            }
            else if (key == Key.N || key == FleetKeys.Cancel)
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
            else if (key == Key.H || key == Key.CursorLeft)
            {
                yes.SetFocus();
                key.Handled = true;
            }
            else if (key == Key.L || key == Key.CursorRight)
            {
                no.SetFocus();
                key.Handled = true;
            }
        };

        var bar = new FleetActionBar(Pos.AnchorEnd(1));

        bar.Show(
        [
            ("y", FleetIcons.Select, () =>
            {
                confirmed = true;
                app.RequestStop(window);
            }),
            ("bksp", FleetIcons.Back, () =>
            {
                FleetModal.Back();
                app.RequestStop(window);
            }),
        ]);

        window.Add(yes, no, bar.Root);
        FleetCorners.Attach(window, () => app.RequestStop(window));

        yes.SetFocus();

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

        return confirmed;
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

    public static T Wait<T>(IApplication app, string title, IReadOnlyList<string> lines, Func<Task<T>> work)
    {
        var window = Sized(title, lines, extraRows: 3);

        var y = 1;
        foreach (var text in lines)
        {
            window.Add(FleetTheme.Caption(2, y, text));
            y++;
        }

        var running = Task.Run(work);
        _ = running.ContinueWith(_ => app.Invoke(() => app.RequestStop(window)), TaskScheduler.Default);

        window.KeyDown += (_, key) => key.Handled = true;

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

        var dismiss = FleetTheme.Primary(2, y + 1, "Dismiss");
        dismiss.Accepting += (_, _) => app.RequestStop(window);

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
