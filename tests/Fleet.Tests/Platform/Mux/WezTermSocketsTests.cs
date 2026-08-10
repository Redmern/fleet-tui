using Fleet.Platform.Mux.WezTerm;

namespace Fleet.Tests.Platform.Mux;

public sealed class WezTermSocketsTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    public WezTermSocketsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private string Socket(string name, DateTime written)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, string.Empty);
        File.SetLastWriteTimeUtc(path, written);

        return path;
    }

    [Fact]
    public void The_most_recently_used_socket_is_tried_first()
    {
        var old = Socket(WezTermSockets.Prefix + "111", new DateTime(2026, 1, 1));
        var recent = Socket(WezTermSockets.Prefix + "222", new DateTime(2026, 8, 10));

        var candidates = WezTermSockets.Candidates(_dir);

        Assert.Equal([recent, old], candidates);
    }

    [Fact]
    public void Only_gui_sockets_are_candidates()
    {
        Socket(WezTermSockets.Prefix + "111", DateTime.UtcNow);
        Socket("something-else", DateTime.UtcNow);

        var candidates = WezTermSockets.Candidates(_dir);

        Assert.All(candidates, c => Assert.Contains(WezTermSockets.Prefix, c));
        Assert.Single(candidates);
    }

    [Fact]
    public void A_missing_runtime_directory_yields_no_candidates()
    {
        Assert.Empty(WezTermSockets.Candidates(Path.Combine(_dir, "nope")));
    }

    [Fact]
    public void An_empty_runtime_directory_yields_no_candidates()
    {
        Assert.Empty(WezTermSockets.Candidates(_dir));
    }

    [Fact]
    public void The_runtime_directory_is_where_wezterm_keeps_its_sockets()
    {
        Assert.Contains("wezterm", WezTermSockets.RuntimeDirectory);
    }

    [Fact]
    public void An_inherited_socket_is_used_as_is_rather_than_probed()
    {
        var previous = Environment.GetEnvironmentVariable(WezTermSockets.Variable);

        try
        {
            Environment.SetEnvironmentVariable(WezTermSockets.Variable, "C:/some/gui-sock-1");
            Assert.Equal("C:/some/gui-sock-1", WezTermSockets.FromEnvironment());

            Environment.SetEnvironmentVariable(WezTermSockets.Variable, "   ");
            Assert.Null(WezTermSockets.FromEnvironment());
        }
        finally
        {
            Environment.SetEnvironmentVariable(WezTermSockets.Variable, previous);
        }
    }
}
