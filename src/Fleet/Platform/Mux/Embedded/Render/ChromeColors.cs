using Fleet.Shared.Themes;

namespace Fleet.Platform.Mux.Embedded.Render;

public sealed record ChromeColors(
    uint Crust, uint Lavender, uint Overlay0, uint Text, uint Surface0, uint Yellow, uint Flamingo, uint Blue)
{
    public static ChromeColors From(ThemePalette theme) => new(
        Rgb(theme.Crust),
        Rgb(theme.Lavender),
        Rgb(theme.Overlay0),
        Rgb(theme.Text),
        Rgb(theme.Surface0),
        Rgb(theme.Yellow),
        Rgb(theme.Cursor),
        Rgb(theme.Blue));

    public static uint Rgb(string hex)
    {
        var value = Convert.ToUInt32(HexColor.Normalize(hex)![1..], 16);

        return Cell.Rgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }
}
