using Fleet.Cli.Composition;

namespace Fleet.Tests.Cli;

public sealed class LaunchFolderTests
{
    [Fact]
    public void A_launch_folder_that_exists_is_the_default_root()
    {
        Assert.Equal("C:/repos", Adapters.LaunchFolder("C:/repos", true, "C:/daemon", p => p == "C:/repos"));
    }

    [Fact]
    public void Inside_a_fleetd_pane_a_missing_empty_or_vanished_launch_folder_gives_no_default_root()
    {
        Assert.Null(Adapters.LaunchFolder(null, true, "C:/daemon", _ => true));
        Assert.Null(Adapters.LaunchFolder(string.Empty, true, "C:/daemon", _ => true));
        Assert.Null(Adapters.LaunchFolder("Z:/gone", true, "C:/daemon", _ => false));
    }

    [Fact]
    public void Run_straight_from_a_shell_the_current_folder_is_the_launch_folder()
    {
        Assert.Equal("C:/here", Adapters.LaunchFolder(null, false, "C:/here", p => p == "C:/here"));
        Assert.Null(Adapters.LaunchFolder(null, false, "C:/gone", _ => false));
    }

    [Fact]
    public void Browsing_falls_back_to_the_launch_folder_and_without_one_to_home()
    {
        Assert.Equal("C:/repos", Adapters.BrowseFallback("C:/repos"));
        Assert.Equal(Adapters.HomeDirectory, Adapters.BrowseFallback(null));
    }
}
