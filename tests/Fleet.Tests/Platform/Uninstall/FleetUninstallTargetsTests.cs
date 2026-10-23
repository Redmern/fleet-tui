using Fleet.Platform.Uninstall;
using Fleet.Ports.Uninstall.Enums;

namespace Fleet.Tests.Platform.Uninstall;

public sealed class FleetUninstallTargetsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"fleet-uninstall-{Guid.NewGuid():N}");

    public FleetUninstallTargetsTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Discovers_the_running_binary()
    {
        var binary = Path.Combine(_dir, "fleet");
        File.WriteAllText(binary, "exe");

        var items = new FleetUninstallTargets(binary).Discover();

        var item = Assert.Single(items);
        Assert.Equal(UninstallKind.Binary, item.Kind);
        Assert.Equal(binary, item.Path);
    }

    [Fact]
    public void A_missing_binary_is_not_in_the_plan() =>
        Assert.Empty(new FleetUninstallTargets(Path.Combine(_dir, "gone")).Discover());

    [Fact]
    public void Removing_the_binary_takes_it_off_its_path()
    {
        var binary = Path.Combine(_dir, "fleet");
        File.WriteAllText(binary, "exe");
        var targets = new FleetUninstallTargets(binary);

        targets.Remove(Assert.Single(targets.Discover()));

        Assert.False(File.Exists(binary));

        // Windows cannot delete a running exe, so it is set aside as <path>.old-<utc> instead.
        if (!OperatingSystem.IsWindows())
        {
            Assert.Empty(Directory.GetFiles(_dir));
        }
    }
}
