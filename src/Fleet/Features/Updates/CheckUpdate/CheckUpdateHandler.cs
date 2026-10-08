using Fleet.Features.Updates.CheckUpdate.Models;
using Fleet.Ports.Releases;
using Fleet.Ports.Releases.Models;
using Fleet.Shared.Releases;

namespace Fleet.Features.Updates.CheckUpdate;

public sealed class CheckUpdateHandler(
    IReleaseClient releases, IUpdateCheckCache? cache = null, Func<DateTimeOffset>? now = null)
{
    public static readonly TimeSpan CheckAtMostEvery = TimeSpan.FromHours(1);

    private DateTimeOffset Now => now?.Invoke() ?? DateTimeOffset.UtcNow;

    public async Task<UpdateCheck> HandleAsync(
        string repo, string currentVersion, CancellationToken ct = default)
    {
        var latest = await releases.LatestAsync(repo, ct).ConfigureAwait(false);

        if (latest is null)
        {
            return new UpdateCheck(
                false,
                null,
                $"no release found at github.com/{repo}/releases (check FLEET_REPO, "
                + "and that a release has been published)");
        }

        cache?.Save(new CachedUpdateCheck(latest.Tag, Now));

        return From(latest.Tag, currentVersion);
    }

    public async Task<UpdateCheck> HandleCachedAsync(
        string repo, string currentVersion, CancellationToken ct = default)
    {
        var saved = cache?.Load();

        if (saved is not null && Fresh(saved))
        {
            return From(saved.Latest, currentVersion);
        }

        var check = await HandleAsync(repo, currentVersion, ct).ConfigureAwait(false);

        return check.Error is not null && saved is not null
            ? From(saved.Latest, currentVersion)
            : check;
    }

    public UpdateCheck? Cached(string currentVersion) =>
        cache?.Load() is { } saved ? From(saved.Latest, currentVersion) : null;

    private bool Fresh(CachedUpdateCheck saved)
    {
        var age = Now - saved.CheckedAt;

        return age >= TimeSpan.Zero && age < CheckAtMostEvery;
    }

    private static UpdateCheck From(string latest, string currentVersion) =>
        new(VersionCompare.IsNewer(latest, currentVersion), latest, null);
}
