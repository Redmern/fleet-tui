using Fleet.Shared.Themes;

namespace Fleet.Ui.Constants;

public static class FleetPalette
{
    public static ThemePalette Current { get; set; } = BuiltInThemes.CatppuccinMocha;

    public static string Crust => Current.Crust;
    public static string Mantle => Current.Mantle;
    public static string Base => Current.Base;
    public static string Surface0 => Current.Surface0;
    public static string Surface1 => Current.Surface1;
    public static string Overlay0 => Current.Overlay0;
    public static string Subtext0 => Current.Subtext0;
    public static string Text => Current.Text;
    public static string Blue => Current.Blue;
    public static string Lavender => Current.Lavender;
    public static string Green => Current.Green;
    public static string Yellow => Current.Yellow;
    public static string Red => Current.Red;
}
