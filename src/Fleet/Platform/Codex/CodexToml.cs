using System.Text;
using Fleet.Ports.Claude.Models;
using Fleet.Shared.Results;

namespace Fleet.Platform.Codex;

public static class CodexToml
{
    private const string Servers = "mcp_servers";

    private const string Projects = "projects";

    private const string TrustLevel = "trust_level";

    private const string TrustedLine = TrustLevel + " = \"trusted\"";

    private sealed record Entry(int Index, bool IsHeader, IReadOnlyList<string> Table, IReadOnlyList<string> Path, string Value);

    private sealed class ScanState
    {
        public int Depth { get; set; }

        public string? OpenMultiline { get; set; }

        public bool Continues => Depth > 0 || OpenMultiline is not null;
    }

    public static Result<string> WithServer(string text, McpServerEntry entry)
    {
        var (lines, newline) = Lines(text);

        if (Scan(lines) is not { } entries)
        {
            return Result<string>.Fail("it is not TOML fleet can edit");
        }

        IReadOnlyList<string> own = [Servers, entry.Name];

        if (entries.Any(e => !e.IsHeader && !StartsWith(e.Table, own) && (Equal(e.Path, [Servers]) || StartsWith(e.Path, own))))
        {
            return Result<string>.Fail($"it defines {Servers}.{entry.Name} inline, which fleet does not rewrite");
        }

        var headers = entries.Where(e => e.IsHeader).ToList();
        var removed = new SortedSet<int>();

        for (var h = 0; h < headers.Count; h++)
        {
            if (StartsWith(headers[h].Path, own))
            {
                var end = h + 1 < headers.Count ? headers[h + 1].Index : lines.Count;

                for (var i = headers[h].Index; i < end; i++)
                {
                    removed.Add(i);
                }
            }
        }

        var section = ServerSection(entry);
        var kept = lines.Where((_, i) => !removed.Contains(i)).ToList();

        if (removed.Count == 0)
        {
            return Result<string>.Ok(Join(Appended(kept, section), newline, text));
        }

        var at = removed.Min;
        var reachesEnd = removed.Max == lines.Count - 1;
        kept.InsertRange(at, reachesEnd ? section : [.. section, string.Empty]);

        return Result<string>.Ok(Join(kept, newline, text));
    }

    public static Result<string> WithTrustedProject(string text, string folder)
    {
        var (lines, newline) = Lines(text);

        if (Scan(lines) is not { } entries)
        {
            return Result<string>.Fail("it is not TOML fleet can edit");
        }

        IReadOnlyList<string> project = [Projects, folder];
        IReadOnlyList<string> trust = [Projects, folder, TrustLevel];

        if (entries.FirstOrDefault(e => !e.IsHeader && Equal(e.Path, trust)) is { } level)
        {
            return IsTrusted(level.Value)
                ? Result<string>.Ok(text)
                : Result<string>.Fail($"it marks {folder} as {level.Value.Trim()}; fleet leaves that choice to you");
        }

        if (entries.Any(e => !e.IsHeader && (Equal(e.Path, [Projects]) || Equal(e.Path, project))))
        {
            return Result<string>.Fail($"it defines {Projects} inline, which fleet does not rewrite");
        }

        if (entries.FirstOrDefault(e => e.IsHeader && Equal(e.Path, project)) is { } header)
        {
            var inserted = lines.ToList();
            inserted.Insert(header.Index + 1, TrustedLine);
            return Result<string>.Ok(Join(inserted, newline, text));
        }

        return Result<string>.Ok(Join(Appended(lines, [$"[{Projects}.{Quoted(folder)}]", TrustedLine]), newline, text));
    }

    private static List<string> ServerSection(McpServerEntry entry) =>
    [
        $"[{Servers}.{Key(entry.Name)}]",
        $"command = {Quoted(entry.Command)}",
        $"args = [{string.Join(", ", entry.Args.Select(Quoted))}]",
        "default_tools_approval_mode = \"approve\"",
    ];

    private static List<string> Appended(List<string> lines, List<string> section) =>
        lines.Count == 0 ? section
        : lines[^1].Trim().Length == 0 ? [.. lines, .. section]
        : [.. lines, string.Empty, .. section];

    private static (List<string> Lines, string Newline) Lines(string text)
    {
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

        if (lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return (lines, newline);
    }

    private static string Join(List<string> lines, string newline, string original)
    {
        var joined = string.Concat(lines.Select(l => l + newline));
        return Lines(original).Lines.SequenceEqual(lines) ? original : joined;
    }

    private static bool IsTrusted(string value)
    {
        var trimmed = value.TrimStart();
        return trimmed.StartsWith("\"trusted\"", StringComparison.Ordinal)
            || trimmed.StartsWith("'trusted'", StringComparison.Ordinal);
    }

    private static bool Equal(IReadOnlyList<string> a, IReadOnlyList<string> b) => a.SequenceEqual(b);

    private static bool StartsWith(IReadOnlyList<string> path, IReadOnlyList<string> prefix) =>
        path.Count >= prefix.Count && path.Take(prefix.Count).SequenceEqual(prefix);

    private static string Key(string name) =>
        name.Length > 0 && name.All(IsBare) ? name : Quoted(name);

    private static string Quoted(string value)
    {
        var quoted = new StringBuilder("\"");

        foreach (var c in value)
        {
            quoted.Append(c switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ when char.IsControl(c) => $"\\u{(int)c:X4}",
                _ => c.ToString(),
            });
        }

        return quoted.Append('"').ToString();
    }

    private static bool IsBare(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '-';

    private static List<Entry>? Scan(List<string> lines)
    {
        var entries = new List<Entry>();
        var state = new ScanState();
        IReadOnlyList<string> table = [];

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];

            if (state.Continues)
            {
                if (!ScanValue(line, 0, state))
                {
                    return null;
                }

                continue;
            }

            var trimmed = line.TrimStart();

            if (trimmed.Length == 0 || trimmed[0] == '#')
            {
                continue;
            }

            var i = line.Length - trimmed.Length;

            if (trimmed[0] == '[')
            {
                if (Header(line, i) is not { } header)
                {
                    return null;
                }

                table = header;
                entries.Add(new Entry(index, true, header, header, string.Empty));
                continue;
            }

            if (ParseKey(line, ref i) is not { } key || i >= line.Length || line[i] != '=')
            {
                return null;
            }

            var value = line[(i + 1)..];

            if (!ScanValue(line, i + 1, state))
            {
                return null;
            }

            entries.Add(new Entry(index, false, table, [.. table, .. key], value));
        }

        return state.Continues ? null : entries;
    }

    private static List<string>? Header(string line, int i)
    {
        var array = i + 1 < line.Length && line[i + 1] == '[';
        i += array ? 2 : 1;

        if (ParseKey(line, ref i) is not { } key)
        {
            return null;
        }

        var close = array ? "]]" : "]";

        if (string.CompareOrdinal(line, i, close, 0, close.Length) != 0)
        {
            return null;
        }

        var rest = line[(i + close.Length)..].TrimStart();
        return rest.Length == 0 || rest[0] == '#' ? key : null;
    }

    private static List<string>? ParseKey(string line, ref int i)
    {
        var key = new List<string>();

        while (true)
        {
            i = SkipSpace(line, i);

            if (i >= line.Length)
            {
                return null;
            }

            string? part;

            if (line[i] == '"')
            {
                part = BasicString(line, ref i);
            }
            else if (line[i] == '\'')
            {
                var end = line.IndexOf('\'', i + 1);
                part = end < 0 ? null : line[(i + 1)..end];
                i = end + 1;
            }
            else
            {
                var start = i;

                while (i < line.Length && IsBare(line[i]))
                {
                    i++;
                }

                part = i > start ? line[start..i] : null;
            }

            if (part is null)
            {
                return null;
            }

            key.Add(part);
            i = SkipSpace(line, i);

            if (i < line.Length && line[i] == '.')
            {
                i++;
                continue;
            }

            return key;
        }
    }

    private static string? BasicString(string line, ref int i)
    {
        var text = new StringBuilder();

        for (var j = i + 1; j < line.Length; j++)
        {
            var c = line[j];

            if (c == '"')
            {
                i = j + 1;
                return text.ToString();
            }

            if (c != '\\')
            {
                text.Append(c);
                continue;
            }

            if (++j >= line.Length)
            {
                return null;
            }

            switch (line[j])
            {
                case 'n':
                    text.Append('\n');
                    break;
                case 't':
                    text.Append('\t');
                    break;
                case 'r':
                    text.Append('\r');
                    break;
                case 'u' when j + 4 < line.Length && int.TryParse(line.AsSpan(j + 1, 4), System.Globalization.NumberStyles.HexNumber, null, out var code):
                    text.Append((char)code);
                    j += 4;
                    break;
                default:
                    text.Append(line[j]);
                    break;
            }
        }

        return null;
    }

    private static bool ScanValue(string line, int i, ScanState state)
    {
        while (i < line.Length)
        {
            if (state.OpenMultiline is { } closing)
            {
                var end = ClosingOf(line, i, closing);

                if (end < 0)
                {
                    return true;
                }

                state.OpenMultiline = null;
                i = end + closing.Length;
                continue;
            }

            var c = line[i];

            if (c == '#')
            {
                return true;
            }

            if (c is '"' or '\'')
            {
                var triple = new string(c, 3);

                if (string.CompareOrdinal(line, i, triple, 0, 3) == 0)
                {
                    state.OpenMultiline = triple;
                    i += 3;
                    continue;
                }

                if (c == '"' ? BasicString(line, ref i) is null : SkipLiteral(line, ref i) is false)
                {
                    return false;
                }

                continue;
            }

            if (c is '[' or '{')
            {
                state.Depth++;
            }
            else if (c is ']' or '}' && --state.Depth < 0)
            {
                return false;
            }

            i++;
        }

        return true;
    }

    private static bool SkipLiteral(string line, ref int i)
    {
        var end = line.IndexOf('\'', i + 1);
        i = end + 1;
        return end >= 0;
    }

    private static int ClosingOf(string line, int i, string closing)
    {
        for (var j = i; j <= line.Length - closing.Length; j++)
        {
            if (closing[0] == '"' && line[j] == '\\')
            {
                j++;
                continue;
            }

            if (string.CompareOrdinal(line, j, closing, 0, closing.Length) == 0)
            {
                return j;
            }
        }

        return -1;
    }

    private static int SkipSpace(string line, int i)
    {
        while (i < line.Length && line[i] is ' ' or '\t')
        {
            i++;
        }

        return i;
    }
}
