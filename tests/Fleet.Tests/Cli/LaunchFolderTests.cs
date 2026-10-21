using Fleet.Cli.Composition;

namespace Fleet.Tests.Cli;

public sealed class LaunchFolderTests
{
    [Fact]
    public void A_launch_folder_that_exists_is_the_default_root()
    {
        Assert.Equal("C:/repos", Adapters.LaunchFolder("C:/repos", p => p == "C:/repos"));
    }

    [Fact]
    public void A_missing_empty_or_vanished_launch_folder_gives_no_default_root()
    {
        Assert.Null(Adapters.LaunchFolder(null, _ => true));
        Assert.Null(Adapters.LaunchFolder(string.Empty, _ => true));
        Assert.Null(Adapters.LaunchFolder("Z:/gone", _ => false));
    }

    [Fact]
    public void Browsing_falls_back_to_the_launch_folder_and_without_one_to_home()
    {
        Assert.Equal("C:/repos", Adapters.BrowseFallback("C:/repos"));
        Assert.Equal(Adapters.HomeDirectory, Adapters.BrowseFallback(null));
    }
}
