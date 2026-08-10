using System.Collections.ObjectModel;
using Fleet.Ui.Constants;
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
        int selected = 0)
    {
        if (items.Count == 0)
        {
            return null;
        }

        int? result = null;

        var keys = PickerKeys.For(items);

        var window = FleetTheme.Overlay(title);

        var list = FleetTheme.Rows(1, 1, Dim.Fill(2));
        list.SetSource(new ObservableCollection<string>(PickerKeys.Label(items, keys).ToList()));
        list.SelectedItem = Math.Clamp(selected, 0, items.Count - 1);

        FleetKeys.ApplyMotions(list, keymap);

        list.Accepting += (_, e) =>
        {
            result = list.SelectedItem;
            app.RequestStop(window);
            e.Handled = true;
        };

        window.KeyDown += (_, key) =>
        {
            if (key == FleetKeys.Cancel)
            {
                app.RequestStop(window);
                key.Handled = true;
                return;
            }

            for (var i = 0; i < keys.Count; i++)
            {
                if (keys[i].Length == 1 && key == new Key(keys[i]))
                {
                    result = i;
                    app.RequestStop(window);
                    key.Handled = true;
                    return;
                }
            }
        };

        window.Add(list, FleetTheme.HintBar(FleetHints.Picker));

        app.Run(window);
        window.Dispose();

        return result;
    }
}
