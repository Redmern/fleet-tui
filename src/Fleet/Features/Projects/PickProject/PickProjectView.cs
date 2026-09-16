using Fleet.Features.Projects.PickProject.Models;
using Fleet.Ports.Projects.Models;
using Fleet.Shared.Keymap.Enums;
using Fleet.Ui;
using Fleet.Ui.Constants;
using Fleet.Ui.Enums;
using Fleet.Ui.Models;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Fleet.Features.Projects.PickProject;

public static class PickProjectView
{
    public static ProjectPick? Show(
        IApplication app, Keymap keymap, PickProjectHandler picker, PickProjectCallbacks callbacks)
    {
        ProjectPick? chosen = null;
        IReadOnlyList<ProjectChoice> entries = picker.Entries();
        var reserved = Reserved(keymap);
        var accelerators = Accelerators(entries, reserved);
        var prefix = new PrefixRecognizer(keymap);
        var openKey = keymap.KeyFor(FleetAction.OpenProject);

        var window = FleetTheme.Screen("fleet — open a project");

        var header = FleetTheme.SectionHeader(1, 0, "Projects");
        var list = FleetTheme.Rows(1, 1, Dim.Fill(3));

        void Refill(int selected)
        {
            accelerators = Accelerators(entries, reserved);
            FleetRows.Fill(list, Rows(entries, accelerators), selected);
        }

        Refill(0);

        var status = FleetTheme.StatusLine(Pos.AnchorEnd(2));

        FleetKeys.ApplyMotions(list, keymap);
        FleetKeys.ApplyOpen(list, keymap);

        void NewProject()
        {
            var created = callbacks.CreateProject();

            if (created is not null)
            {
                chosen = new ProjectPick(created, NewWindow: false);
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

            Refill(index);
        }

        void OpenAt(int index, bool newWindow)
        {
            if (index < 0 || index >= entries.Count)
            {
                return;
            }

            if (entries[index].IsNew)
            {
                NewProject();
                return;
            }

            chosen = new ProjectPick(entries[index].Project!, newWindow);
            app.RequestStop(window);
        }

        void Accept(bool newWindow = false) => OpenAt(FleetRows.Selected(list), newWindow);

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

            if (key == Key.Enter.WithShift || (openKey.IsValid && key == openKey.WithShift))
            {
                Accept(newWindow: true);
                key.Handled = true;
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

            for (var i = 0; i < accelerators.Length; i++)
            {
                if (accelerators[i].Length != 1)
                {
                    continue;
                }

                var accelerator = new Key(accelerators[i]);

                if (key == accelerator)
                {
                    OpenAt(i, newWindow: false);
                    key.Handled = true;
                    return;
                }

                if (key == accelerator.WithShift)
                {
                    OpenAt(i, newWindow: true);
                    key.Handled = true;
                    return;
                }
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
            ($"{keymap.DisplayFor(FleetAction.OpenProject)}/enter/A-Z", "open", () => Accept()),
            ("SHIFT", "new window", () => Accept(newWindow: true)),
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

    private static IReadOnlySet<char> Reserved(Keymap keymap)
    {
        var reserved = new HashSet<char>();

        foreach (var action in new[]
        {
            FleetAction.MoveDown,
            FleetAction.MoveUp,
            FleetAction.MoveFirst,
            FleetAction.MoveLast,
            FleetAction.OpenProject,
            FleetAction.NewProject,
            FleetAction.RemoveProject,
            FleetAction.Close,
            FleetAction.EditKeybinds,
        })
        {
            var text = keymap.TextFor(action);

            if (text.Length == 1)
            {
                reserved.Add(char.ToLowerInvariant(text[0]));
            }
        }

        return reserved;
    }

    private static string[] Accelerators(
        IReadOnlyList<ProjectChoice> entries, IReadOnlySet<char> reserved)
    {
        var labels = entries.Where(e => !e.IsNew).Select(e => e.Label).ToList();
        var keys = PickerKeys.For(labels, reserved);
        var accelerators = new string[entries.Count];

        var next = 0;

        for (var i = 0; i < entries.Count; i++)
        {
            accelerators[i] = entries[i].IsNew ? string.Empty : keys[next++];
        }

        return accelerators;
    }

    private static IReadOnlyList<FleetRow> Rows(
        IReadOnlyList<ProjectChoice> entries, IReadOnlyList<string> accelerators)
    {
        if (entries.Count == 0)
        {
            return [];
        }

        var nameWidth = entries.Max(e => e.Label.Length);

        return
        [
            .. entries.Select((e, i) => new FleetRow(
                [
                    new FleetSpan(
                        accelerators[i].Length == 0 ? "   " : $"{accelerators[i]}  ",
                        FleetTones.Key),
                    FleetSpan.Plain(e.Label.PadRight(nameWidth)),
                ],
                e.Detail.Length == 0 ? null : [FleetSpan.Muted($"{e.Detail} ")])),
        ];
    }
}
