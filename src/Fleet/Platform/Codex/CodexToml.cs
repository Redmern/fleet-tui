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

    private const string Command = "command";

    private const string Args = "args";

    private const string Approval = "default_tools_approval_mode";

    private sealed record Entry(
        int Index, int End, bool IsHeader, IReadOnlyList<string> Table, IReadOnlyList<string> Path, string Value);

    private sealed class ScanState
    {
        public int Depth { get; set; }

        public string? OpenMultiline { get; set; }

        public bool Continues => Depth > 0 || OpenMultiline is not null;
    }

    private sealed class Document
    {
        private readonly List<string> _endings = [];

        private Document()
        {
        }

        public List<string> Lines { get; } = [];

        private string Newline => _endings.FirstOrDefault(e => e.Length > 0) ?? "\n";

        public static Document Of(string text)
        {
            var document = new Document();
            var start = 0;

            while (start < text.Length)
            {
                var at = text.IndexOf('\n', start);

                if (at < 0)
                {
                    document.Add(text[start..], string.Empty);
                    break;
                }

                var carriage = at > start && text[at - 1] == '\r';
                document.Add(text[start..(carriage ? at - 1 : at)], carriage ? "\r\n" : "\n");
                start = at + 1;
            }

            return document;
        }

        public void Remove(int index, int count)
        {
            Lines.RemoveRange(index, count);
            _endings.RemoveRange(index, count);
        }

        public void InsertAfter(int index, IReadOnlyList<string> lines)
        {
            var newline = Terminated(index);
            Lines.InsertRange(index + 1, lines);
            _endings.InsertRange(index + 1, lines.Select(_ => newline));
        }

        public void Append(IReadOnlyList<string> section)
        {
            if (Lines.Count == 0)
            {
                InsertAfter(-1, section);
                return;
            }

            var separated = Lines[^1].Trim().Length == 0;
            InsertAfter(Lines.Count - 1, separated ? section : [string.Empty, .. section]);
        }

        public string Text() => string.Concat(Lines.Select((line, i) => line + _endings[i]));

        private void Add(string line, string ending)
        {
            Lines.Add(line);
            _endings.Add(ending);
        }

        private string Terminated(int index)
        {
            if (index < 0)
            {
                return Newline;
            }

            if (_endings[index].Length == 0)
            {
                _endings[index] = Newline;
            }

            return _endings[index];
        }
    }

    public static Result<string> WithServer(string text, McpServerEntry entry)
    {
        var document = Document.Of(text);

        if (Scan(document.Lines) is not { } entries)
        {
            return Result<string>.Fail("it is not TOML fleet can edit");
        }

        IReadOnlyList<string> own = [Servers, entry.Name];

        if (entries.Any(e => !e.IsHeader && !StartsWith(e.Table, own) && (Equal(e.Path, [Servers]) || StartsWith(e.Path, own))))
        {
            return Result<string>.Fail($"it defines {Servers}.{entry.Name} inline, which fleet does not rewrite");
        }

        List<string> launch =
        [
            $"{Command} = {Quoted(entry.Command)}",
            $"{Args} = [{string.Join(", ", entry.Args.Select(Quoted))}]",
        ];
        var approval = $"{Approval} = \"approve\"";

        if (entries.FirstOrDefault(e => e.IsHeader && Equal(e.Path, own)) is not { } header)
        {
            document.Append([$"[{Servers}.{Key(entry.Name)}]", .. launch, approval]);
            return Result<string>.Ok(document.Text());
        }

        var keys = entries
            .Where(e => !e.IsHeader && Equal(e.Table, own) && e.Path.Count == own.Count + 1)
            .ToList();

        foreach (var stale in keys.Where(e => e.Path[^1] is Command or Args).OrderByDescending(e => e.Index))
        {
            document.Remove(stale.Index, stale.End - stale.Index + 1);
        }

        document.InsertAfter(header.Index, keys.Any(e => e.Path[^1] == Approval) ? launch : [.. launch, approval]);

        return Result<string>.Ok(document.Text());
    }

    public static Result<string> WithTrustedProject(string text, string folder)
    {
        var document = Document.Of(text);

        if (Scan(document.Lines) is not { } entries)
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
            document.InsertAfter(header.Index, [TrustedLine]);
        }
        else
        {
            document.Append([$"[{Projects}.{Quoted(folder)}]", TrustedLine]);
        }

        return Result<string>.Ok(document.Text());
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

                entries[^1] = entries[^1] with { End = index };
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
                entries.Add(new Entry(index, index, true, header, header, string.Empty));
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

            entries.Add(new Entry(index, index, false, table, [.. table, .. key], value));
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
                case 'b':
                    text.Append('\b');
                    break;
                case 'f':
                    text.Append('\f');
                    break;
                case 'u' when j + 4 < line.Length && int.TryParse(line.AsSpan(j + 1, 4), System.Globalization.NumberStyles.HexNumber, null, out var code):
                    text.Append((char)code);
                    j += 4;
                    break;
                case 'U' when j + 8 < line.Length
                    && int.TryParse(line.AsSpan(j + 1, 8), System.Globalization.NumberStyles.HexNumber, null, out var point)
                    && point is >= 0 and <= 0x10FFFF and not (>= 0xD800 and <= 0xDFFF):
                    text.Append(char.ConvertFromUtf32(point));
                    j += 8;
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
