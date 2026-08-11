using Fleet.Ui.Constants;
using Terminal.Gui.Drawing;

using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Fleet.Ui;

public static class FleetInk
{
    private static readonly Color Pill = new(FleetPalette.Surface0);

    public static Attribute For(string tone, Attribute basis) => tone switch
    {
        FleetTones.Muted => new Attribute(
            new Color(FleetPalette.Subtext0), basis.Background, TextStyle.None),

        FleetTones.Key => new Attribute(
            new Color(FleetPalette.Blue), basis.Background, TextStyle.None),

        FleetTones.PillEdge or FleetTones.ChipEdge =>
            new Attribute(Pill, basis.Background, TextStyle.None),

        FleetTones.ChipKey => OnPill(FleetPalette.Blue),
        FleetTones.ChipLabel => OnPill(FleetPalette.Text),

        FleetTones.BranchName => OnPill(FleetPalette.Text),
        FleetTones.Icon => OnPill(FleetPalette.Lavender),
        FleetTones.Ahead => OnPill(FleetPalette.Green),
        FleetTones.Behind => OnPill(FleetPalette.Yellow),
        FleetTones.Dirty => OnPill(FleetPalette.Red),

        _ => basis,
    };

    private static Attribute OnPill(string foreground) =>
        new(new Color(foreground), Pill, TextStyle.None);
}
