using Fleet.Ports.Releases;
using Fleet.Ports.Releases.Models;

namespace Fleet.Features.Updates.ListReleases;

public sealed class ListReleasesHandler(IReleaseClient releases)
{
    public async Task<IReadOnlyList<ReleaseInfo>> HandleAsync(
        string repo, CancellationToken ct = default) =>
        await releases.ListAsync(repo, ct).ConfigureAwait(false);
}
