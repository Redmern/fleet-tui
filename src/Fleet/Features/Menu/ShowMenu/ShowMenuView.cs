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
        IApplication app, Keymap keymap, IReadOnlyList<FleetMenuItem> items)
    {
        var chosen = FleetAction.None;
        var rows = ShowMenuHandler.Rows(items);

        var window = FleetTheme.Overlay("fleet menu");

        var list = FleetTheme.CenteredRows(
            ShowMenuHandler.Width(rows), ShowMenuHandler.Height(rows));

        FleetRows.Fill(list, rows);

        FleetKeys.ApplyMotions(list, keymap);

        void Accept()
        {
            var index = FleetRows.Selected(list);

            if (index >= 0 && index < items.Count)
            {
                chosen = items[index].Action;
                app.RequestStop(window);
            }
        }

        list.Accepting += (_, e) =>
        {
            Accept();
            e.Handled = true;
        };

        var bar = new FleetActionBar(Pos.AnchorEnd(1));

        bar.Show(
        [
            ("enter", "select", Accept),
            ("q/esc", "close", () => app.RequestStop(window)),
        ]);

        var claim = FleetModal.Enter();

        void Keys(object? sender, Key key)
        {
            if (!FleetModal.Owns(claim))
            {
                return;
            }

            if (key == FleetKeys.Cancel || key == keymap.KeyFor(FleetAction.Close))
            {
                app.RequestStop(window);
                key.Handled = true;
                return;
            }

            for (var i = 0; i < items.Count; i++)
            {
                if (keymap.KeyFor(items[i].Action) == key)
                {
                    chosen = items[i].Action;
                    app.RequestStop(window);
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
