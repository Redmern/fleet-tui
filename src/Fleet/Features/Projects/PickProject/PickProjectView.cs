using System.Collections.ObjectModel;
using Fleet.Features.Projects.PickProject.Models;
using Fleet.Ports.Projects.Models;
using Fleet.Shared.Keymap.Enums;
using Fleet.Ui;
using Fleet.Ui.Enums;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Fleet.Features.Projects.PickProject;

public static class PickProjectView
{
    public static Project? Show(
        IApplication app, Keymap keymap, PickProjectHandler picker, PickProjectCallbacks callbacks)
    {
        Project? chosen = null;
        var entries = picker.Entries();
        var prefix = new PrefixRecognizer(keymap);

        var window = FleetTheme.Screen("fleet — open a project");

        var header = FleetTheme.SectionHeader(1, 0, "Projects");
        var list = FleetTheme.Rows(1, 1, Dim.Fill(3));
        list.SetSource(new ObservableCollection<string>(entries.Select(e => e.Label).ToList()));

        var status = FleetTheme.Caption(1, Pos.AnchorEnd(2), string.Empty);

        FleetKeys.ApplyMotions(list, keymap);
        FleetKeys.ApplyOpen(list, keymap);

        void NewProject()
        {
            var created = callbacks.CreateProject();

            if (created is not null)
            {
                chosen = created;
                app.RequestStop(window);
            }
        }

        void Accept()
        {
            var index = list.SelectedItem ?? -1;
            if (index < 0 || index >= entries.Count)
            {
                return;
            }

            if (entries[index].IsNew)
            {
                NewProject();
                return;
            }

            chosen = entries[index].Project;
            app.RequestStop(window);
        }

        void Dispatch(FleetAction action)
        {
            switch (action)
            {
                case FleetAction.Close:
                    app.RequestStop(window);
                    break;

                case FleetAction.NewProject:
                    NewProject();
                    break;

                case FleetAction.OpenProject:
                    Accept();
                    break;

                case FleetAction.EditKeybinds:
                    callbacks.EditKeybinds();
                    break;

                case FleetAction.OpenMenu:
                    Dispatch(callbacks.ShowMenu());
                    break;
            }
        }

        list.Accepting += (_, e) =>
        {
            Accept();
            e.Handled = true;
        };

        list.KeyDown += (_, key) =>
        {
            var result = prefix.Feed(key);

            if (result.Handled)
            {
                key.Handled = true;

                status.Text = result.Outcome == PrefixOutcome.Armed
                    ? $"{keymap.PrefixText} ..."
                    : string.Empty;

                if (result.Outcome == PrefixOutcome.Action)
                {
                    Dispatch(result.Action);
                }

                return;
            }

            if (key == FleetKeys.Cancel)
            {
                app.RequestStop(window);
                key.Handled = true;
                return;
            }

            var direct = keymap.ActionFor(key);

            if (direct is FleetAction.Close or FleetAction.NewProject)
            {
                Dispatch(direct);
                key.Handled = true;
            }
        };

        window.Add(header, list, status, FleetTheme.HintBar(FleetHintText.Picker(keymap)));

        try
        {
            app.Run(window);
        }
        finally
        {
            window.Dispose();
        }

        return chosen;
    }
}
