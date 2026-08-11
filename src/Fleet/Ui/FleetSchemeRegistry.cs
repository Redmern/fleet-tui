using Fleet.Ui.Constants;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;

using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Fleet.Ui;

public static class FleetSchemeRegistry
{
    public static void Register()
    {
        SchemeManager.AddScheme(FleetSchemes.Screen, Build(
            FleetPalette.Text,
            FleetPalette.Base,
            FleetPalette.Lavender));

        SchemeManager.AddScheme(FleetSchemes.Section, Build(
            FleetPalette.Blue,
            FleetPalette.Base,
            FleetPalette.Lavender));

        SchemeManager.AddScheme(FleetSchemes.Error, Build(
            FleetPalette.Red,
            FleetPalette.Base,
            FleetPalette.Yellow));

        SchemeManager.AddScheme(FleetSchemes.Hint, Build(
            FleetPalette.Overlay0,
            FleetPalette.Base,
            FleetPalette.Blue));

        SchemeManager.AddScheme(FleetSchemes.Status, Build(
            FleetPalette.Subtext0,
            FleetPalette.Base,
            FleetPalette.Subtext0));

        SchemeManager.AddScheme(FleetSchemes.Chip, new Scheme
        {
            Normal = Attr(FleetPalette.Text, FleetPalette.Surface0),
            HotNormal = Attr(FleetPalette.Blue, FleetPalette.Surface0),
            Focus = Attr(FleetPalette.Crust, FleetPalette.Blue),
            HotFocus = Attr(FleetPalette.Crust, FleetPalette.Lavender),
            Active = Attr(FleetPalette.Crust, FleetPalette.Blue),
            HotActive = Attr(FleetPalette.Crust, FleetPalette.Lavender),
            Highlight = Attr(FleetPalette.Crust, FleetPalette.Lavender),
            Editable = Attr(FleetPalette.Text, FleetPalette.Surface0),
            ReadOnly = Attr(FleetPalette.Subtext0, FleetPalette.Surface0),
            Disabled = Attr(FleetPalette.Overlay0, FleetPalette.Surface0),
        });

        SchemeManager.AddScheme(FleetSchemes.Accent, new Scheme
        {
            Normal = Attr(FleetPalette.Crust, FleetPalette.Blue),
            HotNormal = Attr(FleetPalette.Crust, FleetPalette.Lavender),
            Focus = Attr(FleetPalette.Crust, FleetPalette.Lavender),
            HotFocus = Attr(FleetPalette.Crust, FleetPalette.Green),
            Active = Attr(FleetPalette.Crust, FleetPalette.Lavender),
            HotActive = Attr(FleetPalette.Crust, FleetPalette.Green),
            Highlight = Attr(FleetPalette.Crust, FleetPalette.Green),
            Editable = Attr(FleetPalette.Crust, FleetPalette.Blue),
            ReadOnly = Attr(FleetPalette.Crust, FleetPalette.Blue),
            Disabled = Attr(FleetPalette.Overlay0, FleetPalette.Surface0),
        });
    }

    private static Scheme Build(string foreground, string background, string hot) => new()
    {
        Normal = Attr(foreground, background),
        HotNormal = Attr(hot, background),
        Focus = Attr(foreground, FleetPalette.Surface1),
        HotFocus = Attr(hot, FleetPalette.Surface1),
        Active = Attr(foreground, FleetPalette.Surface0),
        HotActive = Attr(hot, FleetPalette.Surface0),
        Highlight = Attr(hot, FleetPalette.Surface0),
        Editable = Attr(foreground, FleetPalette.Surface0),
        ReadOnly = Attr(FleetPalette.Subtext0, background),
        Disabled = Attr(FleetPalette.Overlay0, background),
    };

    private static Attribute Attr(string foreground, string background)
        => new(foreground, background, TextStyle.None);
}
