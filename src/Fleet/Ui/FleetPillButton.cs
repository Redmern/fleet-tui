using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Settings.Enums;
using Fleet.Ui.Constants;
using Fleet.Ui.Models;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace Fleet.Ui;

public sealed record FleetPillButton(
    string Icon, string Label, string Mnemonic, Action Activate, string Tone = FleetTones.ChipLabel)
{
    private static readonly FleetAction[] Motions =
    [
        FleetAction.MoveDown,
        FleetAction.MoveUp,
        FleetAction.MoveFirst,
        FleetAction.MoveLast,
        FleetAction.PageDown,
        FleetAction.PageUp,
    ];

    private static readonly Key[] ListKeys =
        [FleetKeys.Back, Key.Enter];

    public FleetRow Row() => Row(FleetButtonHints.Mode);

    public FleetRow Row(ButtonHints hints)
    {
        var spans = new List<FleetSpan>
        {
            new(FleetGlyphs.PillLeft, FleetTones.ChipEdge),
            new($" {Face(hints)} ", Tone),
        };

        if (Mnemonic.Length > 0)
        {
            spans.Add(new FleetSpan($" {FleetKeyText.Display(Mnemonic)} ", FleetTones.ChipKey));
        }

        spans.Add(new FleetSpan(FleetGlyphs.PillRight, FleetTones.ChipEdge));

        return new FleetRow(spans);
    }

    private string Face(ButtonHints hints) =>
        Label.Length == 0 ? FleetButtonHints.Face(Icon, hints) : $"{Icon} {Label}";

    public bool Answers(Key key) =>
        Parsed is { IsValid: true } own && key == own;

    public bool Clashes(Keymap keymap) =>
        Parsed is { IsValid: true } own
        && (ListKeys.Contains(own) || Motions.Any(m => keymap.KeyFor(m) == own));

    public static void Bind(ListView list, IReadOnlyList<FleetPillButton> pills)
    {
        var rows = list as FleetList
            ?? throw new ArgumentException("Pill mnemonics need a FleetList.", nameof(list));

        rows.Pressing += (_, key) =>
        {
            if (key.Handled || pills.FirstOrDefault(p => p.Answers(key)) is not { } pill)
            {
                return;
            }

            pill.Activate();
            key.Handled = true;
        };
    }

    private Key? Parsed
    {
        get
        {
            if (Mnemonic.Length == 0)
            {
                return null;
            }

            try
            {
                return new Key(Mnemonic);
            }
            catch (Exception e) when (e is ArgumentException or FormatException)
            {
                return null;
            }
        }
    }
}
