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

    public static int FitRows(int height) =>
        height + (2 * ShowMenuHandler.Padding) + Header + Footer + FleetCorners.Rows;

    public static (ListView List, FleetActionBar Bar) Place(int width, int height)
    {
        var list = FleetTheme.CenteredRows(width, height);

        return (list, new FleetActionBar(Pos.Bottom(list) + ShowMenuHandler.Padding));
    }

    public static FleetAction Show(
        IApplication app, Keymap keymap, IReadOnlyList<FleetMenuItem> items) =>
        Show(app, keymap, "fleet menu", items, toggle: null);

    public static FleetAction Show(
        IApplication app,
        Keymap keymap,
        string title,
        IReadOnlyList<FleetMenuItem> items,
        Func<FleetAction, string?>? toggle,
        Func<bool>? showKeys = null,
        Func<ButtonHints>? buttonHints = null)
    {
        var chosen = FleetAction.None;
        var shown = items.ToList();

        var gaps = ShowMenuHandler.Gaps(shown);
        var rows = ShowMenuHandler.Rows(shown);
        var keyedHeaders = ShowMenuHandler.Headers(shown);
        var width = ShowMenuHandler.Width([.. rows, .. keyedHeaders.Values]);
        var height = ShowMenuHandler.Height(rows, keyedHeaders.Count, gaps.Count);
        var (list, bar) = Place(width, height);

        var window = FleetTheme.Overlay(
            title,
            Math.Max(Math.Max(width + 20, 52), FleetActionBar.Measure(Buttons(Nothing, Nothing), ButtonHints.Text) + 4),
            FitRows(height));

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

            for (var i = 0; i < shown.Count; i++)
            {
                if (keymap.KeyFor(shown[i].Action) == key)
                {
                    Choose(i);
                    key.Handled = true;
                    return;
                }
            }
        }

        app.Keyboard.KeyDown += Keys;

        window.Add(list, bar.Root);
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
