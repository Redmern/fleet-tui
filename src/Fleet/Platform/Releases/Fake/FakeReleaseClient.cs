using Fleet.Ports.Releases;
using Fleet.Ports.Releases.Models;

namespace Fleet.Platform.Releases.Fake;

public sealed class FakeReleaseClient : IReleaseClient
{
    public ReleaseInfo? Release { get; set; }

    public Dictionary<string, byte[]> Downloads { get; } = [];

    public Task<ReleaseInfo?> LatestAsync(string repo, CancellationToken ct = default) =>
        Task.FromResult(Release);

    public Task<byte[]?> DownloadAsync(string url, CancellationToken ct = default) =>
        Task.FromResult(Downloads.TryGetValue(url, out var bytes) ? bytes : null);
}
