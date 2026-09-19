using Fleet.Ports.Releases;
using Fleet.Ports.Releases.Models;

namespace Fleet.Platform.Releases.Fake;

public sealed class FakeReleaseClient : IReleaseClient
{
    public ReleaseInfo? Release { get; set; }

    public Dictionary<string, ReleaseInfo> Versions { get; } = [];

    public Dictionary<string, byte[]> Downloads { get; } = [];

    public Task<ReleaseInfo?> LatestAsync(string repo, CancellationToken ct = default) =>
        Task.FromResult(Release);

    public Task<ReleaseInfo?> ForVersionAsync(
        string repo, string version, CancellationToken ct = default) =>
        Task.FromResult(Versions.TryGetValue(version.TrimStart('v', 'V'), out var release)
            ? release
            : null);

    public IReadOnlyList<ReleaseInfo> List { get; set; } = [];

    public Task<IReadOnlyList<ReleaseInfo>> ListAsync(string repo, CancellationToken ct = default) =>
        Task.FromResult(List);

    public Task<byte[]?> DownloadAsync(string url, CancellationToken ct = default) =>
        Task.FromResult(Downloads.TryGetValue(url, out var bytes) ? bytes : null);
}
