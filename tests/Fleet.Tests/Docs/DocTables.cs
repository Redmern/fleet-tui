using System.Text.RegularExpressions;

namespace Fleet.Tests.Docs;

// Reads the key tables of README.md and docs/DESIGN.md. A table is found by its two header cells,
// never by its backticks, so a status-glyph or MCP-tool table is not mistaken for a key table.
public static class DocTables
{
    private static readonly Regex Ticks = new("`([^`]+)`", RegexOptions.Compiled);

    private static readonly Regex Range = new("`(\\d)`-`(\\d)`", RegexOptions.Compiled);

    public static IReadOnlyList<string> ReadmeKeys { get; } =
        [.. Rows("README.md", "Key", "Does").SelectMany(r => Tokens(r[0]))];

    public static IReadOnlyList<string> DesignDirectKeys { get; } =
        [.. Rows("docs/DESIGN.md", "Key without the prefix", "Does").SelectMany(r => ModifiedKeys(r[0]))];

    public static IReadOnlyList<string> ReadmePrefixChords { get; } =
    [
        .. Rows("README.md", "Group", "Keys")
            .SelectMany(r => Chords(Tokens(r[0]).FirstOrDefault() ?? string.Empty, Tokens(r[1]).SelectMany(Split))),
    ];

    public static IReadOnlyList<string> DesignPrefixChords { get; } =
    [
        .. Rows("docs/DESIGN.md", "Prefix key", "Does")
            .SelectMany(r => r[0].Split(" › ", 2) is [var group, var keys]
                ? Chords(Tokens(group).First(), Tokens(keys).SelectMany(t => t.Split(' ')).SelectMany(Dash))
                : Tokens(r[0]).SelectMany(t => t.Split(' '))),
    ];

    public static IReadOnlyList<(string File, string First, string Second)> KeyTableHeaders { get; } =
    [
        .. new[] { "README.md", "docs/DESIGN.md" }.SelectMany(f => Headers(f).Select(h => (f, h.First, h.Second))),
    ];

    private static IEnumerable<(string First, string Second)> Headers(string file)
    {
        var lines = File.ReadAllLines(RepoFiles.Path(file));

        for (var i = 0; i + 1 < lines.Length; i++)
        {
            if (Cells(lines[i].TrimEnd()) is { Length: >= 2 } header
                && lines[i + 1].StartsWith("|---", StringComparison.Ordinal)
                && Regex.IsMatch(header[0], "^(Key|Chord|Group|Prefix key)", RegexOptions.IgnoreCase))
            {
                yield return (header[0], header[1]);
            }
        }
    }

    private static IReadOnlyList<string[]> Rows(string file, string firstHeader, string secondHeader)
    {
        var lines = File.ReadAllLines(RepoFiles.Path(file)).Select(l => l.TrimEnd()).ToList();

        for (var i = 0; i + 1 < lines.Count; i++)
        {
            if (Cells(lines[i]) is { Length: >= 2 } header
                && header[0].StartsWith(firstHeader, StringComparison.Ordinal)
                && header[1] == secondHeader
                && lines[i + 1].StartsWith("|---", StringComparison.Ordinal))
            {
                return [.. lines.Skip(i + 2).TakeWhile(l => l.StartsWith('|')).Select(Cells)];
            }
        }

        throw new Xunit.Sdk.XunitException($"{file} has no table headed '{firstHeader}' | '{secondHeader}'");
    }

    private static string[] Cells(string line) =>
        line.StartsWith('|') ? [.. line.Trim('|').Split('|').Select(c => c.Trim())] : [];

    private static IEnumerable<string> Tokens(string cell) =>
        Ticks.Matches(Range.Replace(cell, m => string.Join(' ', Enumerable.Range(m.Groups[1].Value[0], m.Groups[2].Value[0] - m.Groups[1].Value[0] + 1).Select(c => $"`{(char)c}`"))))
            .Select(m => m.Groups[1].Value);

    private static IEnumerable<string> ModifiedKeys(string cell)
    {
        var modifier = Regex.Match(cell, "^`(ctrl|alt)` \\+ `([^`]+)`$");

        return modifier.Success
            ? modifier.Groups[2].Value.Split(' ').Select(k => $"{modifier.Groups[1].Value}+{k}")
            : Tokens(cell);
    }

    private static IEnumerable<string> Chords(string prefix, IEnumerable<string> keys) =>
        keys.Select(k => (prefix.Length == 0 ? string.Empty : prefix + " ") + k);

    private static IEnumerable<string> Split(string token) =>
        token.Length > 1 && token.Contains('/') ? token.Split('/') : [token];

    private static IEnumerable<string> Dash(string key) =>
        key == "1–9" ? Enumerable.Range('1', 9).Select(c => ((char)c).ToString()) : [key];
}
