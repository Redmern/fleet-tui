using Fleet.Shared;

namespace Fleet.Tests.Shared;

public class HomePathTests
{
    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    [Fact]
    public void Contract_then_expand_round_trips_a_path_under_home()
    {
        var path = Path.Combine(Home, "repos", "backend");

        var contracted = HomePath.Contract(path);

        Assert.StartsWith("~", contracted);
        Assert.Equal(path, HomePath.Expand(contracted));
    }

    [Fact]
    public void Contract_reduces_the_home_directory_itself_to_a_tilde()
        => Assert.Equal("~", HomePath.Contract(Home));

    [Fact]
    public void Contract_leaves_a_path_outside_home_alone()
    {
        // Not Path.GetTempPath(): on Windows that is
        // C:\Users\<user>\AppData\Local\Temp, which IS under home, so Contract
        // would correctly return a "~" path and the test would be asserting the
        // wrong thing. Anchor off the filesystem root instead — outside home on
        // both platforms.
        var root = Path.GetPathRoot(Environment.CurrentDirectory)
                   ?? Path.DirectorySeparatorChar.ToString();
        var path = Path.Combine(root, "fleet-outside-home");

        Assert.Equal(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)),
            HomePath.Contract(path));
    }

    [Fact]
    public void Expand_leaves_a_path_without_a_tilde_alone()
    {
        const string path = "/var/lib/fleet";
        Assert.Equal(path, HomePath.Expand(path));
    }

    [Fact]
    public void Expand_does_not_treat_a_tilde_inside_a_name_as_home()
    {
        const string path = "repos/~backup";
        Assert.Equal(path, HomePath.Expand(path));
    }
}
