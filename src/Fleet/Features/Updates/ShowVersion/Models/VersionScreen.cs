using Fleet.Ports.Releases.Models;

namespace Fleet.Features.Updates.ShowVersion.Models;

public sealed record VersionScreen(
    string Current, string? Latest, bool UpdateAvailable, IReadOnlyList<ReleaseInfo> Releases);
