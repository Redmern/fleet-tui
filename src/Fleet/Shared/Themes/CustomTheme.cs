namespace Fleet.Shared.Themes;

public static class CustomTheme
{
    public const string Extension = ".toml";

    public static ThemePalette FromToml(string name, string text)
    {
        var values = ThemeToml.Parse(text);
        var basis = values.TryGetValue("inherits", out var inherits)
            ? BuiltInThemes.Find(inherits) ?? BuiltInThemes.CatppuccinMocha
            : BuiltInThemes.CatppuccinMocha;

        var theme = basis with
        {
            Name = ThemeNames.Normalize(name),
            Title = values.TryGetValue("title", out var title) && title.Length > 0 ? title : name,
            Light = values.TryGetValue("light", out var light)
                ? string.Equals(light, "true", StringComparison.OrdinalIgnoreCase)
                : basis.Light,
        };

        foreach (var role in ThemePalette.RoleNames)
        {
            if (HexColor.Normalize(values.GetValueOrDefault(role)) is { } color)
            {
                theme = theme.WithRole(role, color);
            }
        }

        var ansi = theme.Ansi.Concat(theme.Brights).ToArray();

        for (var i = 0; i < ansi.Length; i++)
        {
            ansi[i] = HexColor.Normalize(values.GetValueOrDefault($"color{i}")) ?? ansi[i];
        }

        return theme with
        {
            Ansi = ansi[..ThemePalette.AnsiCount],
            Brights = ansi[ThemePalette.AnsiCount..],
        };
    }
}
