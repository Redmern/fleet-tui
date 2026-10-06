using Fleet.Features.Menu.ShowMenu.Models;
using Fleet.Shared.Keymap.Enums;
using Fleet.Ui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Fleet.Features.Menu.ShowMenu;

public static class ShowMenuView
{
    public static FleetAction Show(
        IApplication app, Keymap keymap, IReadOnlyList<FleetMenuItem> items) =>
        Show(app, keymap, "fleet menu", items, toggle: null);

    public static FleetAction Show(
        IApplication app,
        Keymap keymap,
        string title,
        IReadOnlyList<FleetMenuItem> items,
        Func<FleetAction, string?>? toggle)
    {
        var chosen = FleetAction.None;
        var shown = items.ToList();
        var rows = ShowMenuHandler.Rows(shown);
        var headers = ShowMenuHandler.Headers(shown);

        var width = ShowMenuHandler.Width([.. rows, .. headers.Values]);
        var height = ShowMenuHandler.Height(rows, headers.Count);

        var window = FleetTheme.Overlay(title, Math.Max(width + 20, 52), height + 6);

        var list = FleetTheme.CenteredRows(width, height);

        FleetRows.Fill(list, rows, headersBefore: headers);

        FleetKeys.ApplyMotions(list, keymap);

        void Choose(int index)
        {
            if (shown[index].Toggles && toggle is not null)
            {
                shown[index] = shown[index] with { Value = toggle(shown[index].Action) };
                FleetRows.Fill(list, ShowMenuHandler.Rows(shown), index, headersBefore: headers);
                list.SetNeedsDraw();
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

        list.Accepting += (_, e) =>
        {
            Accept();
            e.Handled = true;
        };

        var bar = new FleetActionBar(Pos.AnchorEnd(2), alignRight: true);

        bar.Show(
        [
            ("enter", "select", Accept),
            ("q/esc", "close", () => app.RequestStop(window)),
            ("bksp", "back", () =>
            {
                FleetModal.Back();
                app.RequestStop(window);
            }),
        ]);

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
                    FleetRows.Select(list, i);
                    Choose(i);
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

        return chosen;
    }
}
