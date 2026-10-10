using Fleet.Features.Updates.CheckUpdate;
using Fleet.Features.Updates.ListReleases;
using Fleet.Features.Updates.RunUpdate;
using Fleet.Features.Updates.RunUpdate.Models;
using Fleet.Features.Updates.ShowVersion.Models;
using Fleet.Ports.Releases.Models;
using Fleet.Shared.Constants;
using Fleet.Shared.Releases;
using Fleet.Ui;
using Terminal.Gui.App;

namespace Fleet.Cli.Composition;

public static class UpdateWiring
{
    public const string ReloadNote =
        "Open dashboards restart on the new build by themselves; other open fleet windows pick it up when reopened.";

    public const string UpdatedElsewhere =
        "fleet was already updated from another window. Reopen this window to use the new build.";

    public static CheckUpdateHandler Checker() => new(Adapters.Releases(), Adapters.UpdateChecks(), iso: Adapters.Iso());

    public static string? CachedNotice(FreshBuild build) =>
        build.Replaced ? null : Checker().Cached(Adapters.ReleaseRepo, FleetVersion.Current)?.Notice(FleetVersion.Current);

    public static async Task<string?> CheckNoticeAsync(FreshBuild build)
    {
        var check = await Checker()
            .HandleCachedAsync(Adapters.ReleaseRepo, FleetVersion.Current)
            .ConfigureAwait(false);

        return build.Replaced ? null : check.Notice(FleetVersion.Current);
    }

    public static async Task<VersionScreen> VersionScreenAsync(CancellationToken ct)
    {
        if (Adapters.Iso().Load().On)
        {
            return Screen(FleetVersion.Current, null, []);
        }

        try
        {
            var check = await Checker()
                .HandleCachedAsync(Adapters.ReleaseRepo, FleetVersion.Current, ct)
                .ConfigureAwait(false);
            var releases = await new ListReleasesHandler(Adapters.Releases())
                .HandleAsync(Adapters.ReleaseRepo, ct)
                .ConfigureAwait(false);

            return Screen(FleetVersion.Current, check.Latest, releases);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return Screen(FleetVersion.Current, null, []);
        }
    }

    public static VersionScreen Screen(string current, string? checkedLatest, IReadOnlyList<ReleaseInfo> releases)
    {
        var latest = releases.FirstOrDefault(r => !r.Prerelease)?.Tag ?? checkedLatest;

        return new VersionScreen(
            current, latest, latest is not null && VersionCompare.IsNewer(latest, current), releases);
    }

    public static bool Install(IApplication app, FreshBuild build, string? version = null)
    {
        if (build.Replaced)
        {
            FleetDialog.Inform(app, "Update fleet", [UpdatedElsewhere]);
            return false;
        }

        var target = version ?? "the latest release";

        if (!FleetDialog.Confirm(
                app,
                "Update fleet?",
                [
                    $"Download {target} and install it over v{FleetVersion.Current}.",
                    "Running agents and panes keep running. Esc cancels the download.",
                ],
                "Update"))
        {
            return false;
        }

        var (installed, lines) = FleetDialog.Wait(
            app, "Updating fleet", [$"Downloading {target}..."], ct => InstallAsync(version, ct));

        FleetDialog.Inform(app, installed ? "fleet updated" : "Update fleet", lines);

        return installed;
    }

    public static async Task<(bool Installed, IReadOnlyList<string> Lines)> InstallAsync(
        string? version, CancellationToken ct = default)
    {
        try
        {
            var result = await new RunUpdateHandler(Adapters.Releases(), Adapters.Installer())
                .HandleAsync(
                    new RunUpdateCommand(
                        Adapters.ReleaseRepo,
                        FleetVersion.Current,
                        ReleaseAssetNames.ForCurrentPlatform(),
                        Adapters.Executable,
                        version),
                    ct)
                .ConfigureAwait(false);

            if (!result.Succeeded)
            {
                return (false, [$"Update failed: {result.Error}"]);
            }

            if (!result.Value.Installed)
            {
                return (false, [Sentence(result.Value.Message)]);
            }

            var installed = result.Value.Version!.TrimStart('v', 'V');

            return (true, [.. AfterInstall(result.Value, await EmbeddedWiring.StaleDaemonAsync(installed).ConfigureAwait(false))]);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return (false, [$"Update failed: {e.Message}"]);
        }
    }

    public static IEnumerable<string> AfterInstall(UpdateOutcome outcome, string? staleDaemon)
    {
        yield return $"Updated to {outcome.Version}.";
        yield return ReloadNote;

        if (staleDaemon is not null)
        {
            yield return staleDaemon;
        }
    }

    private static string Sentence(string message) =>
        message.Length == 0 ? message : char.ToUpperInvariant(message[0]) + message[1..];
}
