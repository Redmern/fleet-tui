using Fleet.Cli.Composition;
using Fleet.Features.Updates.RunUpdate.Models;
using Fleet.Ports.Releases.Models;

namespace Fleet.Tests.Cli;

public sealed class UpdateWiringTests : IDisposable
{
    private readonly string _executable = Path.Combine(Path.GetTempPath(), $"fleet-fresh-{Guid.NewGuid():N}.exe");

    public UpdateWiringTests()
    {
        File.WriteAllText(_executable, "old build");
        File.SetLastWriteTimeUtc(_executable, DateTime.UtcNow.AddMinutes(-5));
    }

    public void Dispose() => File.Delete(_executable);

    [Fact]
    public void After_an_install_the_notes_name_the_version_and_say_dashboards_restart()
    {
        var lines = UpdateWiring.AfterInstall(new UpdateOutcome(true, "updated", "v0.6.0.27"), null).ToList();

        Assert.Equal(["Updated to v0.6.0.27.", UpdateWiring.ReloadNote], lines);
    }

    [Fact]
    public void After_an_install_a_fleetd_on_the_old_build_is_named_last()
    {
        var lines = UpdateWiring.AfterInstall(new UpdateOutcome(true, "updated", "v0.6.0.27"), "fleetd is still on v0.6.0.26").ToList();

        Assert.Equal("fleetd is still on v0.6.0.26", lines[^1]);
    }

    [Fact]
    public void A_running_build_sees_when_its_executable_is_replaced()
    {
        var build = new FreshBuild(_executable);

        Assert.False(build.Replaced);

        File.WriteAllText(_executable, "new build");
        File.SetLastWriteTimeUtc(_executable, DateTime.UtcNow);

        Assert.True(build.Replaced);
    }

    [Fact]
    public void A_dashboard_waits_until_the_new_executable_has_stopped_changing()
    {
        var build = new FreshBuild(_executable);

        File.WriteAllText(_executable, "new build");
        File.SetLastWriteTimeUtc(_executable, DateTime.UtcNow);

        Assert.False(build.ReplacedAndSettled);

        File.SetLastWriteTimeUtc(_executable, DateTime.UtcNow - FreshBuild.SettleFor - TimeSpan.FromSeconds(1));

        Assert.True(build.ReplacedAndSettled);
    }

    [Fact]
    public void The_version_screen_takes_latest_from_the_live_list_over_an_older_cached_check()
    {
        var screen = UpdateWiring.Screen(
            "0.6.0.26",
            "v0.6.0.26",
            [new ReleaseInfo("v0.7.0-rc.1", [], Prerelease: true), new ReleaseInfo("v0.6.0.27", [])]);

        Assert.Equal("v0.6.0.27", screen.Latest);
        Assert.True(screen.UpdateAvailable);
    }

    [Fact]
    public void Offline_the_version_screen_falls_back_to_the_cached_check()
    {
        var screen = UpdateWiring.Screen("0.6.0.26", "v0.6.0.27", []);

        Assert.Equal("v0.6.0.27", screen.Latest);
        Assert.True(screen.UpdateAvailable);
    }

    [Fact]
    public void A_missing_executable_mid_replace_does_not_count_as_replaced()
    {
        var build = new FreshBuild(_executable);

        File.Delete(_executable);

        Assert.False(build.Replaced);
        File.WriteAllText(_executable, string.Empty);
    }
}
