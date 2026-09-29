using System.Globalization;
using System.Text;

namespace Fleet.Platform.Profiles;

public static class PowerShellData
{
    public static object? Parse(string text)
    {
        var at = 0;
        var value = Value(text, ref at);
        Skip(text, ref at);
        return at == text.Length ? value : throw Fail(text, at, "unexpected text after the value");
    }

    private static object? Value(string text, ref int at)
    {
        Skip(text, ref at);
        if (at >= text.Length)
        {
            throw Fail(text, at, "a value was expected");
        }

        if (text[at] == '@' && at + 1 < text.Length && text[at + 1] == '{')
        {
            at += 2;
            return Table(text, ref at);
        }

        if (text[at] == '@' && at + 1 < text.Length && text[at + 1] == '(')
        {
            at += 2;
            return List(text, ref at);
        }

        return text[at] switch
        {
            '\'' => SingleQuoted(text, ref at),
            '"' => DoubleQuoted(text, ref at),
            '$' => Variable(text, ref at),
            _ => Bare(text, ref at),
        };
    }

    private static Dictionary<string, object?> Table(string text, ref int at)
    {
        var table = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        while (true)
        {
            Skip(text, ref at, separators: true);
            if (at >= text.Length)
            {
                throw Fail(text, at, "'}' was expected");
            }

            if (text[at] == '}')
            {
                at++;
                return table;
            }

            var key = text[at] switch
            {
                '\'' => SingleQuoted(text, ref at),
                '"' => DoubleQuoted(text, ref at),
                _ => Bare(text, ref at),
            };

            Skip(text, ref at);
            if (at >= text.Length || text[at] != '=')
            {
                throw Fail(text, at, $"'=' was expected after '{key}'");
            }

            at++;
            table[key] = Value(text, ref at);
        }
    }

    private static List<object?> List(string text, ref int at)
    {
        var list = new List<object?>();

        while (true)
        {
            Skip(text, ref at, separators: true);
            if (at >= text.Length)
            {
                throw Fail(text, at, "')' was expected");
            }

            if (text[at] == ')')
            {
                at++;
                return list;
            }

            list.Add(Value(text, ref at));
        }
    }

    private static string SingleQuoted(string text, ref int at)
    {
        var value = new StringBuilder();
        at++;

        while (at < text.Length)
        {
            if (text[at] == '\'' && at + 1 < text.Length && text[at + 1] == '\'')
            {
                value.Append('\'');
                at += 2;
            }
            else if (text[at] == '\'')
            {
                at++;
                return value.ToString();
            }
            else
            {
                value.Append(text[at++]);
            }
        }

        throw Fail(text, at, "a string is not closed");
    }

    private static string DoubleQuoted(string text, ref int at)
    {
        var value = new StringBuilder();
        at++;

        while (at < text.Length)
        {
            if (text[at] == '`' && at + 1 < text.Length)
            {
                value.Append(text[at + 1]);
                at += 2;
            }
            else if (text[at] == '"')
            {
                at++;
                return value.ToString();
            }
            else
            {
                value.Append(text[at++]);
            }
        }

        throw Fail(text, at, "a string is not closed");
    }

    private static object? Variable(string text, ref int at)
    {
        var name = Bare(text, ref at);
        return name.ToLowerInvariant() switch
        {
            "$true" => true,
            "$false" => false,
            "$null" => null,
            _ => throw Fail(text, at, $"{name} is not allowed in a data file"),
        };
    }

    private static string Bare(string text, ref int at)
    {
        var start = at;
        while (at < text.Length && !char.IsWhiteSpace(text[at]) && text[at] is not ('=' or ';' or ',' or '}' or ')' or '#'))
        {
            at++;
        }

        return at > start ? text[start..at] : throw Fail(text, at, "a value was expected");
    }

    private static void Skip(string text, ref int at, bool separators = false)
    {
        while (at < text.Length)
        {
            if (char.IsWhiteSpace(text[at]) || (separators && text[at] is ';' or ','))
            {
                at++;
            }
            else if (text[at] == '<' && at + 1 < text.Length && text[at + 1] == '#')
            {
                var end = text.IndexOf("#>", at + 2, StringComparison.Ordinal);
                at = end < 0 ? text.Length : end + 2;
            }
            else if (text[at] == '#')
            {
                while (at < text.Length && text[at] != '\n')
                {
                    at++;
                }
            }
            else
            {
                return;
            }
        }
    }

    private static FormatException Fail(string text, int at, string what)
    {
        var line = 1 + text.AsSpan(0, Math.Min(at, text.Length)).Count('\n');
        return new FormatException(string.Create(CultureInfo.InvariantCulture, $"line {line}: {what}"));
    }
}
