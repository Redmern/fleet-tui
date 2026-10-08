using Fleet.Features.Updates.CheckUpdate;
using Fleet.Features.Updates.CheckUpdate.Models;
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

    public static CheckUpdateHandler Checker() => new(Adapters.Releases(), Adapters.UpdateChecks());

    public static UpdateCheck? Cached() => Checker().Cached(FleetVersion.Current);

    public static Task<UpdateCheck> CheckAsync(CancellationToken ct = default) =>
        Checker().HandleCachedAsync(Adapters.ReleaseRepo, FleetVersion.Current, ct);

    public static Task<IReadOnlyList<ReleaseInfo>> ReleasesAsync(CancellationToken ct = default) =>
        new ListReleasesHandler(Adapters.Releases()).HandleAsync(Adapters.ReleaseRepo, ct);

    public static async Task<VersionScreen> VersionScreenAsync()
    {
        var check = await CheckAsync().ConfigureAwait(false);
        var releases = await ReleasesAsync().ConfigureAwait(false);

        return new VersionScreen(FleetVersion.Current, check.Latest, check.UpdateAvailable, releases);
    }

    public static bool Install(IApplication app, string? version = null)
    {
        var target = version ?? "the latest release";

        if (!FleetDialog.Confirm(
                app,
                "Update fleet?",
                [
                    $"Download {target} and install it over v{FleetVersion.Current}.",
                    "Running agents and panes keep running.",
                ],
                "Update"))
        {
            return false;
        }

        var (installed, lines) = FleetDialog.Wait(
            app, "Updating fleet", [$"Downloading {target}..."], () => InstallAsync(version));

        FleetDialog.Inform(app, installed ? "fleet updated" : "Update fleet", lines);

        return installed;
    }

    public static async Task<(bool Installed, IReadOnlyList<string> Lines)> InstallAsync(
        string? version, CancellationToken ct = default)
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

        return result.Value.Installed
            ? (true, [.. AfterInstall(result.Value, await EmbeddedWiring.StaleDaemonAsync().ConfigureAwait(false))])
            : (false, [Sentence(result.Value.Message)]);
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
