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
        IReadOnlyList<ProjectChoice> entries = picker.Entries();
        var prefix = new PrefixRecognizer(keymap);

        var window = FleetTheme.Screen("fleet — open a project");

        var header = FleetTheme.SectionHeader(1, 0, "Projects");
        var list = FleetTheme.Rows(1, 1, Dim.Fill(3));

        FleetRows.Fill(list, PickProjectHandler.Rows(entries));

        var status = FleetTheme.StatusLine(Pos.AnchorEnd(2));

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

        void DropProject()
        {
            var index = FleetRows.Selected(list);

            if (index < 0 || index >= entries.Count || entries[index].IsNew)
            {
                return;
            }

            status.Text = callbacks.RemoveProject(entries[index].Project!) ?? string.Empty;

            entries = picker.Entries();

            FleetRows.Fill(list, PickProjectHandler.Rows(entries), index);
        }

        void Accept()
        {
            var index = FleetRows.Selected(list);
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

                case FleetAction.RemoveProject:
                    DropProject();
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

        var claim = FleetModal.Enter();

        void Keys(object? sender, Key key)
        {
            if (!FleetModal.Owns(claim))
            {
                return;
            }

            var result = prefix.Feed(key);

            if (result.Handled)
            {
                key.Handled = true;

                status.Text = result.Outcome == PrefixOutcome.Armed
                    ? $"{keymap.PrefixDisplay} ..."
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

            if (direct is FleetAction.Close
                or FleetAction.NewProject
                or FleetAction.RemoveProject)
            {
                Dispatch(direct);
                key.Handled = true;
            }
        }

        app.Keyboard.KeyDown += Keys;

        var bar = new FleetActionBar(Pos.AnchorEnd(1));

        bar.Show(
        [
            ($"{keymap.DisplayFor(FleetAction.OpenProject)}/enter", "open", Accept),
            (keymap.DisplayFor(FleetAction.NewProject), "new", NewProject),
            (keymap.DisplayFor(FleetAction.RemoveProject), "remove", DropProject),
            ($"{keymap.DisplayFor(FleetAction.Close)}/esc", "quit", () => app.RequestStop(window)),
        ]);

        window.Add(header, list, status, bar.Root);

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
