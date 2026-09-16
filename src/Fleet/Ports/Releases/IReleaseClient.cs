using Fleet.Ports.Releases.Models;

namespace Fleet.Ports.Releases;

public interface IReleaseClient
{
    Task<ReleaseInfo?> LatestAsync(string repo, CancellationToken ct = default);

    Task<byte[]?> DownloadAsync(string url, CancellationToken ct = default);
}
