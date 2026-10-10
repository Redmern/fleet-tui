using Fleet.Platform.Forwards;

namespace Fleet.Tests.Platform.Forwards;

public sealed class ControlPathsTests
{
    [Fact]
    public void A_short_runtime_dir_is_used_and_a_long_one_falls_back_to_tmp()
    {
        Assert.Equal("/run/user/1000/fleet", ControlPaths.Directory(_ => "/run/user/1000", "red"));
        Assert.Equal("/tmp/fleet-red", ControlPaths.Directory(_ => "/" + new string('x', 90), "red"));
        Assert.Equal("/tmp/fleet-red", ControlPaths.Directory(_ => null, "red"));
    }

    [Fact]
    public void Socket_paths_stay_under_the_socket_limit_and_differ_per_host()
    {
        var one = ControlPaths.For("/run/user/1000/fleet", "build-box.example.com");
        var two = ControlPaths.For("/run/user/1000/fleet", "other");

        Assert.True(ControlPaths.Fits(one));
        Assert.True(one.Length + 17 < 104);
        Assert.NotEqual(one, two);
        Assert.Equal(one, ControlPaths.For("/run/user/1000/fleet", "BUILD-box.example.com"));
    }

    [Fact]
    public void Stale_sockets_are_removed_and_live_ones_kept()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"fleet-cp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "cm-dead"), string.Empty);
            File.WriteAllText(Path.Combine(dir, "cm-live"), string.Empty);
            File.WriteAllText(Path.Combine(dir, "other"), string.Empty);

            var removed = ControlPaths.CleanStale(dir, path => path.EndsWith("cm-live", StringComparison.Ordinal));

            Assert.Equal([Path.Combine(dir, "cm-dead")], removed);
            Assert.True(File.Exists(Path.Combine(dir, "cm-live")));
            Assert.True(File.Exists(Path.Combine(dir, "other")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void A_socket_left_by_a_killed_master_is_reclaimed_before_the_next_master()
    {
        var file = Path.GetTempFileName();
        try
        {
            Assert.False(ControlPaths.Reclaim(file, _ => true));
            Assert.True(File.Exists(file));
            Assert.True(ControlPaths.Reclaim(file, _ => false));
            Assert.False(File.Exists(file));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void A_file_that_is_not_a_listening_socket_does_not_answer()
    {
        var file = Path.GetTempFileName();
        try
        {
            Assert.False(ControlPaths.Answers(file));
        }
        finally
        {
            File.Delete(file);
        }
    }
}
