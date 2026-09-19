using Fleet.Ui.Constants;
using Fleet.Ui.Enums;
using Terminal.Gui.App;
using Terminal.Gui.Input;

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

        window.Add(primary, secondary, FleetTheme.HintBar(FleetHints.Choose));

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

        window.Add(yes, no, FleetTheme.HintBar(FleetHints.Confirm));

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

    public static void Error(IApplication app, string title, string message)
    {
        var lines = Wrap(message);
        var window = Sized(title, lines, extraRows: 5);

        var y = 1;
        foreach (var line in lines)
        {
            window.Add(FleetTheme.ErrorLine(2, y, line));
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
        };

        window.Add(dismiss, FleetTheme.HintBar(FleetHints.Dismiss));

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
        var height = lines.Count + extraRows + 2;

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
