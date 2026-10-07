using Fleet.Shared.Themes.Models;

namespace Fleet.Shared.Themes;

public static class OmarchyTheme
{
    public const string CustomName = "omarchy";

    private static readonly string[] AnsiNames = ["black", "red", "green", "yellow", "blue", "magenta", "cyan", "white"];

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["catppuccin"] = "catppuccin-mocha",
        ["gruvbox"] = "gruvbox-dark",
        ["tokyonight"] = "tokyo-night",
        ["rosepine"] = "rose-pine",
    };

    public static ThemePalette Resolve(OmarchySnapshot snapshot)
    {
        var derived = Derive(snapshot);
        bool? light = derived?.Light ?? (snapshot.LightMarker ? true : null);
        var key = ThemeNames.Normalize(snapshot.Name);
        var builtIn = BuiltInThemes.Find(Aliases.GetValueOrDefault(key, key));

        if (builtIn is not null && (light is null || builtIn.Light == light))
        {
            return builtIn;
        }

        return derived
            ?? throw new InvalidOperationException(
                $"omarchy theme '{snapshot.Name}' matches no built-in theme and has no colors.toml or alacritty.toml");
    }

    public static ThemePalette? Derive(OmarchySnapshot snapshot)
    {
        var colors = Colors(snapshot);

        if (colors.Background is not { } background || colors.Foreground is not { } foreground)
        {
            return null;
        }

        var light = HexColor.IsLight(background);
        var shade = light ? 0.06 : 0.25;
        var fallback = light ? BuiltInThemes.Find("catppuccin-latte")! : BuiltInThemes.CatppuccinMocha;
        var ansi = Enumerable.Range(0, 16)
            .Select(i => colors.Ansi[i] ?? (i < 8 ? fallback.Ansi[i] : fallback.Brights[i - 8]))
            .ToArray();

        return new ThemePalette(
            CustomName,
            $"Omarchy: {snapshot.Name}",
            light,
            HexColor.Mix(background, "#000000", shade),
            HexColor.Mix(background, "#000000", shade / 2),
            background,
            HexColor.Mix(background, foreground, 0.12),
            HexColor.Mix(background, foreground, 0.2),
            HexColor.Mix(background, foreground, 0.45),
            HexColor.Mix(background, foreground, 0.75),
            foreground,
            ansi[4],
            colors.Accent ?? ansi[5],
            ansi[2],
            ansi[3],
            ansi[1],
            colors.Cursor ?? foreground,
            ansi[..8],
            ansi[8..]);
    }

    private static (string? Background, string? Foreground, string? Accent, string? Cursor, string?[] Ansi) Colors(
        OmarchySnapshot snapshot)
    {
        var ansi = new string?[16];

        if (snapshot.ColorsToml is { } colorsToml)
        {
            var values = ThemeToml.Parse(colorsToml);

            for (var i = 0; i < ansi.Length; i++)
            {
                ansi[i] = Hex(values, $"color{i}");
            }

            if (Hex(values, "background") is { } background && Hex(values, "foreground") is { } foreground)
            {
                return (background, foreground, Hex(values, "accent"), Hex(values, "cursor"), ansi);
            }
        }

        if (snapshot.AlacrittyToml is { } alacrittyToml)
        {
            var values = ThemeToml.Parse(alacrittyToml);

            for (var i = 0; i < AnsiNames.Length; i++)
            {
                ansi[i] ??= Hex(values, $"colors.normal.{AnsiNames[i]}");
                ansi[i + 8] ??= Hex(values, $"colors.bright.{AnsiNames[i]}");
            }

            return (
                Hex(values, "colors.primary.background"),
                Hex(values, "colors.primary.foreground"),
                null,
                Hex(values, "colors.cursor.cursor"),
                ansi);
        }

        return (null, null, null, null, ansi);
    }

    private static string? Hex(IReadOnlyDictionary<string, string> values, string key) =>
        HexColor.Normalize(values.GetValueOrDefault(key));
}
