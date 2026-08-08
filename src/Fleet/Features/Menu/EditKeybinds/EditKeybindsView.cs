using System.Collections.ObjectModel;
using Fleet.Ports.Keymap;
using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Keymap.Models;
using Fleet.Ui;
using Fleet.Ui.Constants;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Fleet.Features.Menu.EditKeybinds;

public static class EditKeybindsView
{
    private const string PrefixRow = "prefix";

    public static KeymapConfig Show(IApplication app, IKeymapStore store, Keymap keymap)
    {
        var config = keymap.Config;
        var actions = KeymapDefaults.Configurable;

        var window = FleetTheme.Overlay("Keybinds");
        var list = FleetTheme.Rows(1, 1, Dim.Fill(3));
        var status = FleetTheme.Caption(1, Pos.AnchorEnd(3), string.Empty);

        void Fill()
        {
            var rows = new List<string> { Row("Prefix", FleetKeyText.Display(config.Prefix)) };
            rows.AddRange(actions.Select(a => Row(KeymapDefaults.Describe(a), FleetKeyText.Display(Binding(config, a)))));
            list.SetSource(new ObservableCollection<string>(rows));
        }

        void Rebind()
        {
            var index = list.SelectedItem ?? -1;
            if (index < 0)
            {
                return;
            }

            var target = index == 0 ? PrefixRow : actions[index - 1].ToString();
            var captured = CaptureKeyView.Show(app, target);

            if (captured is null)
            {
                status.Text = "Unchanged.";
                return;
            }

            config = index == 0
                ? config.WithPrefix(captured)
                : config.With(actions[index - 1], captured);

            store.Save(config);
            Fill();
            status.Text = $"Saved. {target} is now {FleetKeyText.Display(captured)}. Reopen panes to apply.";
        }

        Fill();
        FleetKeys.ApplyMotions(list, keymap);

        list.Accepting += (_, e) =>
        {
            Rebind();
            e.Handled = true;
        };

        list.KeyDown += (_, key) =>
        {
            if (key == Key.Esc)
            {
                app.RequestStop(window);
                key.Handled = true;
            }
        };

        window.Add(list, status, FleetTheme.HintBar(FleetHints.Keybinds));

        app.Run(window);
        window.Dispose();

        return config;
    }

    private static string Row(string label, string key) => $"{label.PadRight(24)}   {key}";

    private static string Binding(KeymapConfig config, FleetAction action) =>
        config.Bindings.TryGetValue(action, out var key) ? key : string.Empty;
}
