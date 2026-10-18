using Fleet.Ports.Releases.Models;
using Fleet.Shared.Releases;

namespace Fleet.Features.Updates.ListReleases;

public static class ReleaseListFormatter
{
    public const int ShownByDefault = 10;

    private const string Indent = "  ";

    public static IReadOnlyList<string> Lines(IReadOnlyList<ReleaseInfo> releases, string installed, bool all)
    {
        if (releases.Count == 0)
        {
            return [];
        }

        var latest = releases.FirstOrDefault(r => !r.Prerelease)?.Tag;
        var shown = all ? releases : [.. releases.Where(r => !r.Prerelease).Take(ShownByDefault)];
        var older = shown.Count == 0 ? 0 : releases.Count - 1 - LastIndexOf(releases, shown[^1]);
        var width = shown.Count == 0 ? 0 : shown.Max(r => r.Tag.Length);

        return
        [
            $"{Indent}Installed  v{installed.TrimStart('v', 'V')}",
            $"{Indent}Latest     {latest ?? "unknown"}",
            string.Empty,
            .. shown.Select(r => $"{Indent}{r.Tag.PadRight(width)}  {Notes(r, installed, latest)}".TrimEnd()),
            .. older > 0 ? [$"{Indent}... {older} older, use --all"] : Array.Empty<string>(),
        ];
    }

    private static int LastIndexOf(IReadOnlyList<ReleaseInfo> releases, ReleaseInfo release)
    {
        for (var i = releases.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(releases[i], release))
            {
                return i;
            }
        }

        return -1;
    }

    private static string Notes(ReleaseInfo release, string installed, string? latest) =>
        string.Join(
            ", ",
            new[]
            {
                VersionCompare.AreEqual(release.Tag, installed) ? "installed" : null,
                latest is not null && release.Tag == latest ? "latest" : null,
                release.Prerelease ? "prerelease" : null,
            }.OfType<string>());
}
