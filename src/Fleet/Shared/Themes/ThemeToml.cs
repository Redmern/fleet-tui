using System.Text;

namespace Fleet.Shared.Themes;

public static class ThemeToml
{
    public static IReadOnlyDictionary<string, string> Parse(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var section = string.Empty;

        foreach (var raw in text.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = StripComment(raw).Trim();

            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line.Trim('[', ']').Trim();
                continue;
            }

            var equals = line.IndexOf('=', StringComparison.Ordinal);

            if (equals <= 0)
            {
                continue;
            }

            var key = line[..equals].Trim().Trim('"', '\'');
            var value = Unquote(line[(equals + 1)..].Trim());

            values[section.Length == 0 ? key : $"{section}.{key}"] = value;
        }

        return values;
    }

    public static string Write(ThemePalette theme)
    {
        var text = new StringBuilder();

        text.Append("title = \"").Append(theme.Title.Replace("\"", "'", StringComparison.Ordinal)).Append("\"\n");
        text.Append("light = ").Append(theme.Light ? "true" : "false").Append('\n');

        foreach (var role in ThemePalette.RoleNames)
        {
            text.Append(role).Append(" = \"").Append(theme.Role(role)).Append("\"\n");
        }

        var ansi = theme.Ansi.Concat(theme.Brights).ToList();

        for (var i = 0; i < ansi.Count; i++)
        {
            text.Append("color").Append(i).Append(" = \"").Append(ansi[i]).Append("\"\n");
        }

        return text.ToString();
    }

    public static string WriteOverrides(ThemePalette theme, ThemePalette basis)
    {
        var text = new StringBuilder();

        text.Append("inherits = \"").Append(basis.Name).Append("\"\n");
        text.Append("title = \"").Append(theme.Title.Replace("\"", "'", StringComparison.Ordinal)).Append("\"\n");
        text.Append("light = ").Append(HexColor.IsLight(theme.Base) ? "true" : "false").Append('\n');

        foreach (var role in ThemePalette.RoleNames)
        {
            AppendChanged(text, role, theme.Role(role), basis.Role(role));
        }

        var ansi = theme.Ansi.Concat(theme.Brights).ToList();
        var inherited = basis.Ansi.Concat(basis.Brights).ToList();

        for (var i = 0; i < ansi.Count; i++)
        {
            AppendChanged(text, $"color{i}", ansi[i], inherited[i]);
        }

        return text.ToString();
    }

    private static void AppendChanged(StringBuilder text, string key, string color, string inherited)
    {
        var normalized = HexColor.Normalize(color);

        if (normalized is not null && normalized != HexColor.Normalize(inherited))
        {
            text.Append(key).Append(" = \"").Append(normalized).Append("\"\n");
        }
    }

    private static string StripComment(string line)
    {
        var quote = '\0';

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == '#')
            {
                return line[..i];
            }
        }

        return line;
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && (value[0] is '"' or '\'') && value[^1] == value[0]
            ? value[1..^1]
            : value;
}
