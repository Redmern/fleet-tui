using Fleet.Shared.Orchestrations;
using Fleet.Ui;
using Fleet.Ui.Constants;
using Fleet.Ui.Models;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;

namespace Fleet.Features.Orchestrations.Dispatch;

public static class NewSubView
{
    public const string Title = $"{FleetIcons.NewSub} New sub-orchestrator";

    public const string EmptyTask = "Type a task for the sub first.";

    public const string NoPast = "no earlier tasks";

    public const string NoMatch = "no earlier task matches";

    public const int TaskSlot = 0;

    public const int PastSlot = 1;

    private const int AreaRows = 6;

    private const int MostPastRows = 8;

    private const int RowWidth = FleetCard.Width - 6;

    public static string? Show(IApplication app, IReadOnlyList<string> past, Keymap keymap)
    {
        string? result = null;
        var pastRows = Math.Clamp(past.Count, 1, MostPastRows);
        var height = AreaRows + pastRows + 10 + FleetCorners.Rows;
        var window = FleetTheme.Modal(Title, FleetCard.Width, height);

        var taskHeader = Header(0);
        var area = new FleetTextArea { X = 2, Y = 1, Width = Dim.Fill(2), Height = AreaRows };
        var error = FleetTheme.ErrorText(2, AreaRows + 1);
        var pastHeader = Header(AreaRows + 2);
        var list = FleetTheme.Rows(1, AreaRows + 3, pastRows + 3);
        IReadOnlyList<int> shown = [];
        var filling = false;

        void Close() => app.RequestStop(window);

        void Submit()
        {
            if (Problem(area.Value) is { } problem)
            {
                error.Text = problem;
                area.SetFocus();
                return;
            }

            result = Dispatched(area.Value);
            Close();
        }

        void Back()
        {
            FleetModal.Back();
            Close();
        }

        var pills = Pills(Submit, Close);
        var keys = Keys(keymap, pills);

        void Refill(int selected)
        {
            shown = Shown(past, area.Value);
            var rows = PastRows(past, shown, RowWidth);
            FleetRows.Fill(list, [.. rows, .. pills.Select(p => p.Row())], selected, gapsAfter: [rows.Count - 1]);
        }

        void Pick(int index)
        {
            if (index < 0 || index >= shown.Count)
            {
                return;
            }

            filling = true;

            try
            {
                area.Text = past[shown[index]];
            }
            finally
            {
                filling = false;
            }

            area.MoveEnd();
            area.SetFocus();
        }

        void Act(int slot)
        {
            switch (slot)
            {
                case TaskSlot:
                    area.SetFocus();
                    break;

                case PastSlot:
                    list.SetFocus();
                    FleetRows.Select(list, 0);
                    break;

                default:
                    pills[slot - 2].Activate();
                    break;
            }
        }

        void Activate()
        {
            var index = FleetRows.Selected(list);
            var pastCount = Math.Max(shown.Count, 1);

            if (index < pastCount)
            {
                Pick(index);
            }
            else if (index - pastCount < pills.Count)
            {
                pills[index - pastCount].Activate();
            }
        }

        FleetRows.Fill(taskHeader, [TaskHeader(keys[TaskSlot])]);
        FleetRows.Fill(pastHeader, [PastHeader(past.Count, keys[PastSlot])]);
        taskHeader.SelectedItem = null;
        pastHeader.SelectedItem = null;
        Refill(0);
        FleetKeys.ApplyMotions(list, keymap);

        area.Submitting += (_, _) => Submit();
        area.Cancelling += (_, _) => Close();
        area.GoingBack += (_, _) => Back();
        area.ContentsChanged += (_, _) =>
        {
            error.Text = string.Empty;

            if (!filling)
            {
                Refill(FleetRows.Selected(list));
            }
        };

        if (list is FleetList rows)
        {
            rows.Pressing += (_, key) =>
            {
                if (key.Handled)
                {
                    return;
                }

                if (FleetKeys.Submits(key))
                {
                    Submit();
                    key.Handled = true;
                }
                else if (FleetCard.Jump(key, keys, pills) is { } slot)
                {
                    Act(slot);
                    key.Handled = true;
                }
            };
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
                Close();
                key.Handled = true;
            }
            else if (FleetKeys.Submits(key))
            {
                Submit();
                key.Handled = true;
            }
            else if (!area.HasFocus && FleetKeys.GoesBack(key))
            {
                Back();
                key.Handled = true;
            }
        };

        var bar = new FleetActionBar(Pos.AnchorEnd(1));

        bar.Show(
        [
            ("enter", FleetIcons.Select, Activate),
            ("ctrl+enter", FleetIcons.Dispatch, Submit),
            ("tab", FleetIcons.History, () => (area.HasFocus ? (View)list : area).SetFocus()),
            ("bksp", FleetIcons.Back, Back),
        ]);

        window.Add(
            taskHeader,
            area,
            error,
            pastHeader,
            list,
            FleetTheme.Caption(1, Pos.AnchorEnd(2), "Enter adds a line. The sub plans the task and starts agents."),
            bar.Root);
        FleetCorners.Attach(window, Close);

        FleetModal.Enter();

        try
        {
            area.SetFocus();
            app.Run(window);
        }
        finally
        {
            FleetModal.Leave();
            window.Dispose();
        }

        return result;
    }

    public static IReadOnlyList<FleetPillButton> Pills(Action dispatch, Action cancel) =>
        FleetCard.Pills(FleetIcons.Dispatch, "Dispatch", "d", dispatch, cancel);

    public static IReadOnlyList<string> Keys(Keymap keymap, IReadOnlyList<FleetPillButton> pills) =>
        FleetMnemonics.For(["Task", "Past tasks"], keymap, pills.Select(p => p.Mnemonic.ToLowerInvariant()));

    public static FleetRow TaskHeader(string key) =>
        FleetCard.FieldRow(new FleetCardField(FleetIcons.Task, "Task", string.Empty, () => { }), key);

    public static FleetRow PastHeader(int count, string key) =>
        FleetCard.FieldRow(
            new FleetCardField(
                FleetIcons.History,
                "Past",
                count switch
                {
                    0 => NoPast,
                    1 => "1 earlier task",
                    _ => $"{count} earlier tasks",
                },
                () => { }),
            key);

    public static IReadOnlyList<int> Shown(IReadOnlyList<string> past, string typed) =>
        PickerFilter.Apply(PickerEntry.Plain(past), PromptLine.OneLine(typed));

    public static IReadOnlyList<FleetRow> PastRows(IReadOnlyList<string> past, IReadOnlyList<int> shown, int width)
    {
        if (shown.Count == 0)
        {
            return [new FleetRow([new FleetSpan(past.Count == 0 ? NoPast : NoMatch, FleetTones.Muted)])];
        }

        return
        [
            .. shown.Select(i => new FleetRow(
            [
                new FleetSpan(FleetIcons.History, FleetTones.Icon),
                FleetSpan.Plain(" "),
                FleetSpan.Plain(PickerFilter.OneLine(PromptLine.OneLine(past[i]), width - 2)),
            ])),
        ];
    }

    public static bool Dispatchable(string task) => !string.IsNullOrWhiteSpace(task);

    public static string? Problem(string task) => Dispatchable(task) ? null : EmptyTask;

    public static string Dispatched(string task) => FleetTextArea.Normalize(task).Trim();

    private static FleetList Header(int y)
    {
        var header = (FleetList)FleetTheme.Rows(1, y, 1);

        header.CanFocus = false;

        return header;
    }
}
