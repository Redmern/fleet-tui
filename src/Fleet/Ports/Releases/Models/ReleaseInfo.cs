namespace Fleet.Ports.Releases.Models;

public sealed record ReleaseInfo(
    string Tag, IReadOnlyList<ReleaseAsset> Assets, bool Prerelease = false);
