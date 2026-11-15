using Fleet.Features.Menu.ShowMenu.Models;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Settings.Enums;
using Fleet.Ui;
using Fleet.Ui.Constants;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Fleet.Features.Menu.ShowMenu;

public static class ShowMenuView
{
    public const int Footer = 1;

    public const int Header = Footer;

    public const int NoteRows = 2;

    private const int NoteMargin = 4;

    private const int NarrowestMenu = 52;

    private const int RoomBesideRows = 20;

    public static int FitRows(int height, bool noted = false) =>
        height + (2 * ShowMenuHandler.Padding) + Header + Footer + FleetCorners.Rows + (noted ? NoteRows : 0);

    public static int FitCols(int rowsWidth, int barWidth, string? note) =>
        Math.Max(
            Math.Max(Math.Max(rowsWidth + RoomBesideRows, NarrowestMenu), barWidth + 4),
            note is null ? 0 : note.Length + NoteMargin);

    public static (ListView List, FleetActionBar Bar) Place(int width, int height, bool noted = false)
    {
        var list = FleetTheme.CenteredRows(width, height, noted ? NoteRows : 0);

        return (list, new FleetActionBar(Pos.Bottom(list) + ShowMenuHandler.Padding + (noted ? NoteRows : 0)));
    }

    public static Label PlaceNote(ListView list, string note)
    {
        var line = FleetTheme.FullLine(Pos.Bottom(list) + 1, 1);
        line.Text = note;
        line.TextAlignment = Alignment.Center;

        return line;
    }

    public static FleetAction Show(
        IApplication app, Keymap keymap, IReadOnlyList<FleetMenuItem> items) =>
        Show(app, keymap, "fleet menu", items, toggle: null);

    public sealed record Composed(Window Window, ListView List, FleetActionBar Bar, IReadOnlyList<FleetMenuItem> Items);

    public static Composed Compose(Keymap keymap, MenuScreen screen, Func<FleetAction, string?> value) =>
        Compose(screen.Title, new ShowMenuHandler(keymap).Items(screen.Sections, value), screen.Note);

    public static Composed Compose(string title, IReadOnlyList<FleetMenuItem> items, string? note)
    {
        var rows = ShowMenuHandler.Rows(items);
        var keyedHeaders = ShowMenuHandler.Headers(items);
        var width = ShowMenuHandler.Width([.. rows, .. keyedHeaders.Values]);
        var height = ShowMenuHandler.Height(rows, keyedHeaders.Count, ShowMenuHandler.Gaps(items).Count);
        var (list, bar) = Place(width, height, note is not null);

        var window = FleetTheme.Overlay(
            title,
            FitCols(width, FleetActionBar.Measure(Buttons(Nothing, Nothing), ButtonHints.Text), note),
            FitRows(height, note is not null));

        window.Add(list, bar.Root);
        if (note is not null)
        {
            window.Add(PlaceNote(list, note));
        }

        return new Composed(window, list, bar, items);
    }

    public static int RowFor(IReadOnlyList<FleetMenuItem> items, Keymap keymap, Key key)
    {
        if (key == Key.Empty)
        {
            return -1;
        }

        for (var i = 0; i < items.Count; i++)
        {
            if (keymap.KeyFor(items[i].Action) is var bound && bound != Key.Empty && bound == key)
            {
                return i;
            }
        }

        return -1;
    }

    public static FleetAction Show(
        IApplication app,
        Keymap keymap,
        MenuScreen screen,
        Func<FleetAction, string?> value,
        Func<FleetAction, string?>? toggle,
        Func<bool>? showKeys = null,
        Func<ButtonHints>? buttonHints = null) =>
        Run(app, keymap, Compose(keymap, screen, value), toggle, showKeys, buttonHints);

    public static FleetAction Show(
        IApplication app,
        Keymap keymap,
        string title,
        IReadOnlyList<FleetMenuItem> items,
        Func<FleetAction, string?>? toggle,
        Func<bool>? showKeys = null,
        Func<ButtonHints>? buttonHints = null) =>
        Run(app, keymap, Compose(title, items, note: null), toggle, showKeys, buttonHints);

    private static FleetAction Run(
        IApplication app,
        Keymap keymap,
        Composed composed,
        Func<FleetAction, string?>? toggle,
        Func<bool>? showKeys,
        Func<ButtonHints>? buttonHints)
    {
        var chosen = FleetAction.None;
        var shown = composed.Items.ToList();
        var gaps = ShowMenuHandler.Gaps(shown);
        var (window, list, bar, _) = composed;

        void Refill(int index)
        {
            var keyed = FleetKeyHints.Shown;
            var current = ShowMenuHandler.Rows(shown, keyed);
            var headers = ShowMenuHandler.Headers(shown, keyed);

            list.Width = ShowMenuHandler.Width([.. current, .. headers.Values]);
            FleetRows.Fill(list, current, index, gapsAfter: gaps, headersBefore: headers);
            bar.Show(Buttons(Accept, () =>
            {
                FleetModal.Back();
                app.RequestStop(window);
            }));
            list.SetNeedsLayout();
            list.SetNeedsDraw();
        }

        Refill(0);

        FleetKeys.ApplyMotions(list, keymap);

        void Choose(int index)
        {
            if (shown[index].Toggles && toggle is not null)
            {
                shown[index] = shown[index] with { Value = toggle(shown[index].Action) };
                if (showKeys is not null)
                {
                    FleetKeyHints.Apply(showKeys());
                }

                if (buttonHints is not null)
                {
                    FleetButtonHints.Apply(buttonHints());
                }

                Refill(index);
                return;
            }

            chosen = shown[index].Action;
            app.RequestStop(window);
        }

        void Accept()
        {
            var index = FleetRows.Selected(list);

            if (index >= 0 && index < shown.Count)
            {
                Choose(index);
            }
        }

        void Hints() => Refill(FleetRows.Selected(list));

        FleetKeyHints.Changed += Hints;

        list.Accepting += (_, e) =>
        {
            Accept();
            e.Handled = true;
        };

        var claim = FleetModal.Enter();

        void Keys(object? sender, Key key)
        {
            if (!FleetModal.Owns(claim))
            {
                return;
            }

            if (FleetKeys.GoesBack(key))
            {
                FleetModal.Back();
                app.RequestStop(window);
                key.Handled = true;
                return;
            }

            if (key == FleetKeys.Cancel || key == keymap.KeyFor(FleetAction.Close))
            {
                app.RequestStop(window);
                key.Handled = true;
                return;
            }

            if (RowFor(shown, keymap, key) is var row and >= 0)
            {
                Choose(row);
                key.Handled = true;
            }
        }

        app.Keyboard.KeyDown += Keys;

        FleetCorners.Attach(window, () => app.RequestStop(window));

        try
        {
            app.Run(window);
        }
        finally
        {
            FleetModal.Leave();
            FleetKeyHints.Changed -= Hints;
            app.Keyboard.KeyDown -= Keys;
            window.Dispose();
        }

        return chosen;
    }

    private static IReadOnlyList<(string Key, string Label, Action Run)> Buttons(Action accept, Action back) =>
    [
        ("enter", FleetIcons.Select, accept),
        ("bksp", FleetIcons.Back, back),
    ];

    private static void Nothing()
    {
    }
}
