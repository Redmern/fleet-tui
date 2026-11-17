using System.Text.RegularExpressions;

namespace Fleet.Shared.Status;

public static partial class PaneSettled
{
    private const string BusyLine = "esc to interrupt";

    public static string Text(string paneText, IReadOnlyList<Regex>? volatilePatterns = null)
    {
        var kept = string.Join('\n', paneText
            .ReplaceLineEndings("\n")
            .Split('\n')
            .Where(line => !line.Contains(BusyLine, StringComparison.OrdinalIgnoreCase)));

        foreach (var pattern in volatilePatterns ?? [])
        {
            kept = pattern.Replace(kept, string.Empty);
        }

        foreach (var pattern in Common)
        {
            kept = pattern.Replace(kept, string.Empty);
        }

        return Whitespace().Replace(kept, string.Empty);
    }

    private static IReadOnlyList<Regex> Common { get; } =
        [Clock(), Elapsed(), Tokens(), Traffic(), Cost(), BrailleSpinner(), AsciiSpinner(), BorderLine(), SideBorder()];

    [GeneratedRegex(
        @"(?<![\w.:/\\-])\d{1,2}:\d{2}(?::\d{2}(?:\.\d+)?)?(?:Z|[ \t]?[AaPp][Mm])?(?![\w:])",
        RegexOptions.CultureInvariant)]
    private static partial Regex Clock();

    [GeneratedRegex(@"(?<![\w.,])(?:\d+(?:\.\d+)?(?:ms|s|sec)|\d+(?:m|min|h))+\b", RegexOptions.CultureInvariant)]
    private static partial Regex Elapsed();

    [GeneratedRegex(@"\b\d[\d.,]*\s?[kKmM]?\s?tokens?\b", RegexOptions.CultureInvariant)]
    private static partial Regex Tokens();

    [GeneratedRegex(@"[↑↓]\s?\d[\d.,]*[kKmM]?", RegexOptions.CultureInvariant)]
    private static partial Regex Traffic();

    [GeneratedRegex(@"\$\d[\d.,]*", RegexOptions.CultureInvariant)]
    private static partial Regex Cost();

    [GeneratedRegex(@"[⠀-⣿]", RegexOptions.CultureInvariant)]
    private static partial Regex BrailleSpinner();

    [GeneratedRegex(@"^[ \t]*[|/\\-](?=[ \t])", RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex AsciiSpinner();

    [GeneratedRegex(
        @"^[ \t]*[─-╿▀▄][─-╿▀▄ \t]*$",
        RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex BorderLine();

    [GeneratedRegex(
        @"^[ \t]*[│┃║]|[│┃║][ \t]*$",
        RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex SideBorder();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}
