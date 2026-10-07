using System.Globalization;
using System.Text.RegularExpressions;

namespace Fleet.Shared;

public static partial class NvimVersion
{
    public static Version Minimum { get; } = new(0, 9);

    public static Version? Parse(string? output)
    {
        var match = output is null ? null : Pattern().Match(output);

        return match is { Success: true }
            ? new Version(Number(match, 1), Number(match, 2), Number(match, 3))
            : null;
    }

    private static int Number(Match match, int group) =>
        int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);

    public static bool SupportsAppName(Version version) => version >= Minimum;

    [GeneratedRegex(@"NVIM v(\d+)\.(\d+)\.(\d+)")]
    private static partial Regex Pattern();
}
