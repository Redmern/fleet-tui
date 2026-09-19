using Fleet.Cli.Composition;
using Fleet.Cli.Models;
using Fleet.Features.Updates.ListReleases;
using Fleet.Features.Updates.RunUpdate;
using Fleet.Features.Updates.RunUpdate.Models;
using Fleet.Shared.Constants;
using Fleet.Shared.Releases;

namespace Fleet.Cli.Commands;

public static class UpdateCommand
{
    public static async Task<int> RunAsync(Invocation invocation)
    {
        var repo = Adapters.ReleaseRepo;

        if (invocation.ListVersions)
        {
            return await ListAsync(repo).ConfigureAwait(false);
        }

        Console.WriteLine(invocation.Version is null
            ? $"fleet: checking github.com/{repo}/releases for an update..."
            : $"fleet: checking github.com/{repo}/releases for {invocation.Version}...");

        var result = await new RunUpdateHandler(Adapters.Releases(), Adapters.Installer())
            .HandleAsync(new RunUpdateCommand(
                repo,
                FleetVersion.Current,
                ReleaseAssetNames.ForCurrentPlatform(),
                Adapters.Executable,
                invocation.Version))
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            Console.Error.WriteLine($"fleet: {result.Error}");
            return 1;
        }

        Console.WriteLine($"fleet: {result.Value}");
        return 0;
    }

    private static async Task<int> ListAsync(string repo)
    {
        Console.WriteLine($"fleet: releases at github.com/{repo}/releases");

        var releases = await new ListReleasesHandler(Adapters.Releases())
            .HandleAsync(repo)
            .ConfigureAwait(false);

        if (releases.Count == 0)
        {
            Console.WriteLine("  none found (check FLEET_REPO, and that a release has been "
                + "published).");
            return 1;
        }

        foreach (var release in releases)
        {
            var current = VersionCompare.AreEqual(release.Tag, FleetVersion.Current)
                ? " (installed)"
                : string.Empty;
            var prerelease = release.Prerelease ? " (prerelease)" : string.Empty;

            Console.WriteLine($"  {release.Tag}{current}{prerelease}");
        }

        return 0;
    }
}
