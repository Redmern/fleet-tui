using Fleet.Shared;

namespace Fleet.Tests.Shared;

public class PathKeyTests
{
    [Fact]
    public void A_wezterm_cwd_matches_the_recorded_worktree_despite_the_separators()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.True(PathKey.Same(
            "C:/repos/techweb/backend/test/",
            @"C:\repos\techweb\backend\test"));
    }

    [Fact]
    public void Case_does_not_matter_on_windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.True(PathKey.Same(@"C:\Repos\TechWeb", @"c:\repos\techweb"));
    }

    [Fact]
    public void A_trailing_separator_is_not_a_different_directory()
    {
        Assert.True(PathKey.Same(
            Path.Combine("a", "b") + Path.DirectorySeparatorChar,
            Path.Combine("a", "b")));
    }

    [Fact]
    public void Different_directories_stay_different()
    {
        Assert.False(PathKey.Same(Path.Combine("a", "b"), Path.Combine("a", "c")));
    }

    [Fact]
    public void An_empty_path_matches_only_another_empty_one()
    {
        Assert.Equal(string.Empty, PathKey.For(string.Empty));
        Assert.False(PathKey.Same(string.Empty, "a"));
    }
}
