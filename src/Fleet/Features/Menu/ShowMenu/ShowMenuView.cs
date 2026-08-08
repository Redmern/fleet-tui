using System.Collections.ObjectModel;
using Fleet.Features.Menu.ShowMenu.Models;
using Fleet.Shared.Keymap.Enums;
using Fleet.Ui;
using Fleet.Ui.Constants;
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

        var list = FleetTheme.Rows(1, 1, Dim.Fill(2));
        list.SetSource(new ObservableCollection<string>(rows.ToList()));

        FleetKeys.ApplyMotions(list, keymap);
        FleetKeys.ApplyOpen(list, keymap);

        void Accept()
        {
            var index = list.SelectedItem ?? -1;

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

        list.KeyDown += (_, key) =>
        {
            if (key == Key.Esc)
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
        };

        window.Add(list, FleetTheme.HintBar(FleetHints.Menu));

        app.Run(window);
        window.Dispose();

        return chosen;
    }
}
