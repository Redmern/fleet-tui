using Fleet.Ui.Constants;
using Fleet.Ui.Models;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Fleet.Ui;

public static class FleetCard
{
    public const int Width = 66;

    private const int LabelWidth = 9;

    public static FleetRow FieldRow(FleetCardField field, string mnemonic) =>
        new(
            [
                new FleetSpan(field.Icon, FleetTones.Icon),
                FleetSpan.Plain(" "),
                new FleetSpan($"{field.Label}:".PadRight(LabelWidth), FleetTones.Key),
                FleetSpan.Plain(field.Value),
            ],
            mnemonic.Length == 0 ? null : Chip(mnemonic));

    public static IReadOnlyList<FleetPillButton> Pills(
        string icon, string label, string mnemonic, Action submit, Action cancel) =>
    [
        new(icon, label, mnemonic, submit),
        new(FleetIcons.Close, "Cancel", "Esc", cancel),
    ];

    public static IReadOnlyList<string> FieldKeys(
        IReadOnlyList<FleetCardField> fields, IReadOnlyList<FleetPillButton> pills, Keymap keymap) =>
        FleetMnemonics.For(
            [.. fields.Select(f => f.Label)],
            keymap,
            pills.Select(p => p.Mnemonic.ToLowerInvariant()));

    public static int? Jump(Key key, IReadOnlyList<string> fieldKeys, IReadOnlyList<FleetPillButton> pills)
    {
        for (var i = 0; i < fieldKeys.Count; i++)
        {
            if (fieldKeys[i].Length == 1 && key == new Key(fieldKeys[i]))
            {
                return i;
            }
        }

        for (var i = 0; i < pills.Count; i++)
        {
            if (pills[i].Answers(key))
            {
                return fieldKeys.Count + i;
            }
        }

        return null;
    }

    public static IReadOnlyList<(string Key, string Label, Action Run)> Footer(
        Action activate, Action submit, Action back) =>
    [
        ("enter", FleetIcons.Select, activate),
        ("ctrl+enter", FleetIcons.Create, submit),
        ("bksp", FleetIcons.Back, back),
    ];

    public static bool Show(
        IApplication app,
        string title,
        Keymap keymap,
        Func<IReadOnlyList<FleetCardField>> fields,
        (string Icon, string Label, string Mnemonic) submit,
        string caption = "")
    {
        var submitted = false;
        var shown = fields();
        var height = shown.Count + 3 + 6 + FleetCorners.Rows;
        var window = FleetTheme.Modal(title, Width, height);
        var list = FleetTheme.Rows(1, 1, Dim.Fill(3));

        void Close() => app.RequestStop(window);

        void Submit()
        {
            submitted = true;
            Close();
        }

        void Back()
        {
            FleetModal.Back();
            Close();
        }

        var pills = Pills(submit.Icon, submit.Label, submit.Mnemonic, Submit, Close);
        var keys = FieldKeys(shown, pills, keymap);

        void Refill(int selected)
        {
            shown = fields();
            var rows = shown.Select((f, i) => FieldRow(f, keys[i])).Concat(pills.Select(p => p.Row())).ToList();
            FleetRows.Fill(list, rows, selected, gapsAfter: [shown.Count - 1]);
        }

        void Act(int index)
        {
            Refill(index);

            if (index < shown.Count)
            {
                shown[index].Edit();
                Refill(index);
                return;
            }

            if (index - shown.Count < pills.Count)
            {
                pills[index - shown.Count].Activate();
            }
        }

        void Activate() => Act(FleetRows.Selected(list));

        Refill(0);
        FleetKeys.ApplyMotions(list, keymap);

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
                else if (Jump(key, keys, pills) is { } index)
                {
                    Act(index);
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
            else if (FleetKeys.GoesBack(key))
            {
                Back();
                key.Handled = true;
            }
        };

        var bar = new FleetActionBar(Pos.AnchorEnd(1));
        bar.Show(Footer(Activate, Submit, Back));

        window.Add(list, FleetTheme.Caption(1, Pos.AnchorEnd(2), caption), bar.Root);
        FleetCorners.Attach(window, Close);

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

        return submitted;
    }

    private static IReadOnlyList<FleetSpan> Chip(string mnemonic) =>
    [
        new(FleetGlyphs.PillLeft, FleetTones.ChipEdge),
        new($" {FleetKeyText.Display(mnemonic)} ", FleetTones.ChipKey),
        new(FleetGlyphs.PillRight, FleetTones.ChipEdge),
    ];
}
