using System.Text.RegularExpressions;

namespace Fleet.Features.Repositories.OpenPr;

public static partial class PrUrl
{
    [GeneratedRegex(@"https?://[^\s'""<>]+/pulls?/\d+", RegexOptions.CultureInvariant)]
    private static partial Regex Link();

    [GeneratedRegex(@"refs/pull/(\d+)/head", RegexOptions.CultureInvariant)]
    private static partial Regex PullRef();

    public static string? Find(string output)
    {
        var matches = Link().Matches(output);

        return matches.Count == 0 ? null : matches[^1].Value;
    }

    public static string? Number(string output)
    {
        var match = PullRef().Match(output);

        return match.Success ? match.Groups[1].Value : null;
    }
}
