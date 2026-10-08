using Fleet.Features.Updates.ShowVersion.Models;
using Fleet.Shared.Releases;
using Fleet.Ui.Models;

namespace Fleet.Features.Updates.ShowVersion;

public static class VersionRows
{
    public static IReadOnlyList<string> Summary(VersionScreen screen) =>
    [
        $"Installed  v{screen.Current.TrimStart('v', 'V')}",
        screen switch
        {
            { Latest: null } => "Latest     unknown: could not reach the release page",
            { UpdateAvailable: true } => $"Latest     {screen.Latest}, an update is available",
            _ => $"Latest     {screen.Latest}, you are up to date",
        },
    ];

    public static IReadOnlyList<FleetRow> Rows(VersionScreen screen)
    {
        if (screen.Releases.Count == 0)
        {
            return [];
        }

        var width = screen.Releases.Max(r => r.Tag.Length);

        return
        [
            .. screen.Releases.Select(r => new FleetRow(
                [FleetSpan.Plain(r.Tag.PadRight(width))],
                Notes(screen, r.Tag, r.Prerelease) is { Length: > 0 } notes ? [FleetSpan.Muted($"{notes} ")] : null)),
        ];
    }

    private static string Notes(VersionScreen screen, string tag, bool prerelease) =>
        string.Join(
            ", ",
            new[]
            {
                VersionCompare.AreEqual(tag, screen.Current) ? "installed" : null,
                screen.Latest is { } latest && VersionCompare.AreEqual(tag, latest) ? "latest" : null,
                prerelease ? "prerelease" : null,
            }.OfType<string>());
}
