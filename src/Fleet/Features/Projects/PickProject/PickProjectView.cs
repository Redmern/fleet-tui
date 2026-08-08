using System.Collections.ObjectModel;
using Fleet.Ports.Projects;
using Fleet.Ui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

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

        var window = FleetTheme.Screen("fleet — open a project");

        var panel = FleetTheme.Panel("Projects");
        panel.X = 0;
        panel.Y = 0;
        panel.Width = Dim.Fill();
        panel.Height = Dim.Fill(4);

        var list = FleetTheme.Rows();
        list.SetSource(new ObservableCollection<string>(entries.Select(e => e.Label).ToList()));
        panel.Add(list);

        var open = FleetTheme.Primary(1, Pos.AnchorEnd(3), "_Open");
        var newProject = FleetTheme.Secondary(14, Pos.AnchorEnd(3), "_New project");
        var quit = FleetTheme.Secondary(32, Pos.AnchorEnd(3), "_Quit");

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
                return;   // cancelled; stay in the picker
            }

            chosen = created;
            app.RequestStop(window);
        }

        // Enter raises Accepting, NOT Activated. Activated is a different command
        // (Command.Activate); wiring it meant Enter never reached this handler and
        // no project could be opened.
        list.Accepting += (_, e) =>
        {
            Accept();
            e.Handled = true;
        };

        open.Accepting += (_, _) => Accept();
        newProject.Accepting += (_, _) => NewProject();
        quit.Accepting += (_, _) => app.RequestStop(window);

        // KeyDownNotHandled, not KeyDown: the focused ListView consumes keys via
        // its own bindings first, so a KeyDown handler on the window never saw
        // these. This fires only for keys nothing else claimed, which is also why
        // typing into a field cannot trigger a shortcut.
        window.KeyDownNotHandled += (_, key) =>
        {
            if (key == Key.Q || key == Key.Esc)
            {
                app.RequestStop(window);
                key.Handled = true;
            }
            else if (key == Key.N)
            {
                NewProject();
                key.Handled = true;
            }
        };

        window.Add(panel, open, newProject, quit, FleetTheme.HintBar(
            "enter / o  open      n  new project      q  quit      tab  move focus"));

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
