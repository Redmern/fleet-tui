using Fleet.Platform.Releases;

namespace Fleet.Tests.Platform.Releases;

public sealed class SelfInstallTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"fleet-selfinstall-{Guid.NewGuid():N}");

    public SelfInstallTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        foreach (var file in Directory.GetFiles(_dir))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Replacing_puts_the_new_binary_in_place_and_clears_old_copies()
    {
        var target = Path.Combine(_dir, "fleet.exe");
        File.WriteAllText(target, "old");
        File.WriteAllText(target + ".old-20260101000000", "older");

        new SelfInstall().Replace(target, "new"u8.ToArray());

        Assert.Equal("new", File.ReadAllText(target));
        Assert.Empty(Directory.GetFiles(_dir, "fleet.exe.old-*"));
    }

    [Fact]
    public void A_left_over_copy_that_cannot_be_removed_yet_does_not_fail_the_update()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var target = Path.Combine(_dir, "fleet.exe");
        var inUse = target + ".old-20260101000000";
        File.WriteAllText(target, "old");
        File.WriteAllText(inUse, "still running");

        // Deleting a read-only file throws UnauthorizedAccessException, as deleting a running exe does.
        File.SetAttributes(inUse, FileAttributes.ReadOnly);

        new SelfInstall().Replace(target, "new"u8.ToArray());

        Assert.Equal("new", File.ReadAllText(target));
        Assert.True(File.Exists(inUse));
    }
}
