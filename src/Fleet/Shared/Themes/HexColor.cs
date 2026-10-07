using System.Globalization;

namespace Fleet.Shared.Themes;

public static class HexColor
{
    public static string? Normalize(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var hex = value.Trim();

        if (hex.StartsWith('#'))
        {
            hex = hex[1..];
        }
        else if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            hex = hex[2..];
        }

        return hex.Length == 6 && hex.All(char.IsAsciiHexDigit)
            ? "#" + hex.ToLowerInvariant()
            : null;
    }

    public static string Mix(string from, string to, double amount)
    {
        var (r1, g1, b1) = Channels(from);
        var (r2, g2, b2) = Channels(to);

        return $"#{Blend(r1, r2):x2}{Blend(g1, g2):x2}{Blend(b1, b2):x2}";

        int Blend(int a, int b) => (int)Math.Round(a + ((b - a) * amount));
    }

    public static bool IsLight(string color)
    {
        var (r, g, b) = Channels(color);

        return ((0.299 * r) + (0.587 * g) + (0.114 * b)) / 255 > 0.5;
    }

    private static (int R, int G, int B) Channels(string color)
    {
        var hex = Normalize(color) ?? throw new ArgumentException($"not a hex color: {color}", nameof(color));

        return (Channel(hex, 1), Channel(hex, 3), Channel(hex, 5));
    }

    private static int Channel(string hex, int at) =>
        int.Parse(hex.AsSpan(at, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
}
