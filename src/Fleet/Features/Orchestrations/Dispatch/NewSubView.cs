using Fleet.Ui;
using Fleet.Ui.Constants;
using Fleet.Ui.Models;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Fleet.Features.Orchestrations.Dispatch;

public static class NewSubView
{
    public const string Title = $"{FleetIcons.NewSub} New sub-orchestrator";

    public const string EmptyTask = "(press enter to type the task)";

    public static string? Show(IApplication app, IReadOnlyList<string> past, Keymap keymap)
    {
        string? result = null;
        var task = string.Empty;

        var window = FleetTheme.Overlay(Title);
        var list = FleetTheme.Rows(1, 1, Dim.Fill(3));
        var rows = new List<(FleetRow Row, Action Act)>();

        void Submit()
        {
            if (!Dispatchable(task))
            {
                EditTask();

                return;
            }

            result = task.Trim();
            app.RequestStop(window);
        }

        void EditTask()
        {
            var typed = TypeTask(app, task);

            if (typed is null)
            {
                return;
            }

            task = typed.Value.Text;

            if (typed.Value.Submit && Dispatchable(task))
            {
                Submit();
            }
        }

        void ChoosePast()
        {
            var picked = FleetPicker.Choose(app, "Past tasks", past, keymap);

            if (picked is not null)
            {
                task = past[picked.Value];
            }
        }

        var buttons = Buttons(Submit, () => app.RequestStop(window));

        void Refill(int selected)
        {
            var fields = Fields(task, past.Count);

            rows.Clear();
            rows.Add((fields[0], EditTask));

            if (fields.Count > 1)
            {
                rows.Add((fields[1], ChoosePast));
            }

            rows.AddRange(buttons.Select(b => (b.Row(), b.Activate)));
            FleetRows.Fill(list, [.. rows.Select(r => r.Row)], selected);
        }

        Refill(0);
        FleetKeys.ApplyMotions(list, keymap);
        FleetPillButton.Bind(list, buttons);

        void Activate()
        {
            var index = FleetRows.Selected(list);

            if (index < 0 || index >= rows.Count)
            {
                return;
            }

            rows[index].Act();
            Refill(index);
        }

        list.Accepting += (_, e) =>
        {
            Activate();
            e.Handled = true;
        };

        window.KeyDown += (_, key) =>
        {
            if (key == FleetKeys.Cancel)
            {
                app.RequestStop(window);
                key.Handled = true;
            }
            else if (FleetKeys.GoesBack(key))
            {
                FleetModal.Back();
                app.RequestStop(window);
                key.Handled = true;
            }
        };

        var bar = new FleetActionBar(Pos.AnchorEnd(1));

        bar.Show(
        [
            ("enter", FleetIcons.Select, Activate),
            ("bksp", FleetIcons.Back, () =>
            {
                FleetModal.Back();
                app.RequestStop(window);
            }),
        ]);

        window.Add(
            list,
            FleetTheme.Caption(1, Pos.AnchorEnd(2), "The sub-orchestrator plans the task and starts agents for it."),
            bar.Root);
        FleetCorners.Attach(window, () => app.RequestStop(window));

        FleetModal.Enter();

        try
        {
            app.Run(window);
        }
        finally
        {
            FleetModal.Leave();
            window.Dispose();
        }

        return result;
    }

    public static IReadOnlyList<FleetRow> Fields(string task, int pastTasks)
    {
        var rows = new List<FleetRow>
        {
            FleetFieldRow.Of(FleetIcons.Task, "Task", task.Length == 0 ? EmptyTask : task),
        };

        if (pastTasks > 0)
        {
            rows.Add(FleetFieldRow.Of(
                FleetIcons.History, "Past", pastTasks == 1 ? "1 earlier task" : $"{pastTasks} earlier tasks"));
        }

        return rows;
    }

    public static IReadOnlyList<FleetPillButton> Buttons(Action dispatch, Action cancel) =>
    [
        new(FleetIcons.Dispatch, "Dispatch", "d", dispatch),
        new(FleetIcons.Close, "Cancel", "Esc", cancel),
    ];

    public static bool Dispatchable(string task) => !string.IsNullOrWhiteSpace(task);

    public static bool SubmitsWhileTyping(Key key) => key == Key.Enter.WithCtrl;

    private static (string Text, bool Submit)? TypeTask(IApplication app, string initial)
    {
        (string Text, bool Submit)? result = null;

        var window = FleetTheme.Modal(Title, 70, 8 + FleetCorners.Rows);
        var field = FleetTheme.Field(2, 3, initial);

        void Done(bool submit)
        {
            result = (field.Text.Trim(), submit);
            app.RequestStop(window);
        }

        field.Accepting += (_, e) =>
        {
            e.Handled = true;
            Done(false);
        };

        var bar = new FleetActionBar(Pos.AnchorEnd(1));

        bar.Show(
        [
            ("enter", FleetIcons.Select, () => Done(false)),
            ("ctrl+enter", FleetIcons.Dispatch, () => Done(true)),
            ("bksp", FleetIcons.Back, () => app.RequestStop(window)),
        ]);

        window.Add(FleetTheme.Caption(2, 1, $"{FleetIcons.Task} Task for the sub"), field, bar.Root);
        FleetCorners.Attach(window, () => app.RequestStop(window));

        window.KeyDown += (_, key) =>
        {
            if (key == FleetKeys.Cancel)
            {
                key.Handled = true;
                app.RequestStop(window);
            }
        };

        field.KeyDown += (_, key) =>
        {
            if (SubmitsWhileTyping(key))
            {
                key.Handled = true;
                Done(true);
            }
            else if (FleetKeys.GoesBack(key, field.Text))
            {
                key.Handled = true;
                app.RequestStop(window);
            }
        };

        FleetModal.Enter();

        try
        {
            field.SetFocus();
            app.Run(window);
        }
        finally
        {
            FleetModal.Leave();
            window.Dispose();
        }

        return result;
    }
}
