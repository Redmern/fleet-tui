using Fleet.Features.Updates.CheckUpdate.Models;
using Fleet.Ports.Releases;
using Fleet.Shared.Releases;

namespace Fleet.Features.Updates.CheckUpdate;

public sealed class CheckUpdateHandler(IReleaseClient releases)
{
    public async Task<UpdateCheck> HandleAsync(
        string repo, string currentVersion, CancellationToken ct = default)
    {
        var latest = await releases.LatestAsync(repo, ct).ConfigureAwait(false);

        return latest is null
            ? new UpdateCheck(
                false,
                null,
                $"no release found at github.com/{repo}/releases (check FLEET_REPO, "
                + "and that a release has been published)")
            : new UpdateCheck(VersionCompare.IsNewer(latest.Tag, currentVersion), latest.Tag, null);
    }
}
