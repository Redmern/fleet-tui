using System.Collections.ObjectModel;
using Fleet.Ports.Projects.Models;
using Fleet.Ui;
using Fleet.Ui.Constants;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;

namespace Fleet.Features.Projects.PickProject;

public static class PickProjectView
{
    public static Project? Show(
        IApplication app, PickProjectHandler picker, Func<Project?> createProject)
    {
        Project? chosen = null;
        var entries = picker.Entries();

        var window = FleetTheme.Screen("fleet — open a project");

        var header = FleetTheme.SectionHeader(1, 0, "Projects");
        var list = FleetTheme.Rows(1, 1, Dim.Fill(2));
        list.SetSource(new ObservableCollection<string>(entries.Select(e => e.Label).ToList()));

        FleetKeys.ApplyMotions(list);
        FleetKeys.ApplyOpen(list);

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

        void NewProject()
        {
            var created = createProject();
            if (created is null)
            {
                return;
            }

            chosen = created;
            app.RequestStop(window);
        }

        list.Accepting += (_, e) =>
        {
            Accept();
            e.Handled = true;
        };

        list.KeyDown += (_, key) =>
        {
            if (key == FleetKeys.Quit || key == FleetKeys.Cancel)
            {
                app.RequestStop(window);
                key.Handled = true;
            }
            else if (key == FleetKeys.New)
            {
                NewProject();
                key.Handled = true;
            }
        };

        window.Add(header, list, FleetTheme.HintBar(FleetHints.Picker));

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
