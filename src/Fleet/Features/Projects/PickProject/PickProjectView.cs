using System.Collections.ObjectModel;
using Fleet.Ports.Projects;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Fleet.Features.Projects.PickProject;

public static class PickProjectView
{
    /// <summary>
    /// Runs the picker. Returns the chosen project, or null if the user quit.
    /// </summary>
    /// <param name="createProject">
    /// Supplied by the composition root. Taken as a callback rather than a
    /// reference to the CreateProject slice, because a slice may not name another
    /// slice — only Program.cs is allowed to know both exist.
    /// </param>
    public static Project? Show(
        IApplication app, PickProjectHandler picker, Func<Project?> createProject)
    {
        Project? chosen = null;
        var entries = picker.Entries();

        var list = new ListView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
        };

        list.SetSource(new ObservableCollection<string>(entries.Select(e => e.Label).ToList()));

        var window = new Window
        {
            Title = "fleet - open a project",
            BorderStyle = LineStyle.Rounded,
        };

        var hint = new Label
        {
            X = 0,
            Y = Pos.AnchorEnd(1),
            Text = "enter open    n new    q quit",
        };

        void Accept()
        {
            var index = list.SelectedItem ?? -1;
            if (index < 0 || index >= entries.Count)
            {
                return;
            }

            if (entries[index].IsNew)
            {
                var created = createProject();
                if (created is null)
                {
                    return;   // cancelled; stay in the picker
                }

                chosen = created;
                app.RequestStop(window);
                return;
            }

            chosen = entries[index].Project;
            app.RequestStop(window);
        }

        list.Activated += (_, _) => Accept();

        window.KeyDown += (_, key) =>
        {
            if (key == Key.Q || key == Key.Esc)
            {
                app.RequestStop(window);
                key.Handled = true;
            }
            else if (key == Key.N)
            {
                list.SelectedItem = entries.Count - 1;
                Accept();
                key.Handled = true;
            }
        };

        window.Add(list, hint);

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
