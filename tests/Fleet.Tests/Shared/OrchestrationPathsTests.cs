using Fleet.Shared.Orchestrations;

namespace Fleet.Tests.Shared;

[Collection(ConfigHomeCollection.Name)]
public sealed class OrchestrationPathsTests : ConfigHomeFixture
{
    [Fact]
    public void The_ready_marker_lives_under_the_config_home_not_the_worktree()
    {
        var folder = Path.Combine(Path.GetTempPath(), "some-worktree");

        var marker = OrchestrationPaths.ReadyMarker(folder);

        Assert.StartsWith(Path.Combine(ConfigHome, "ready"), marker);
        Assert.DoesNotContain("some-worktree", marker);
    }

    [Fact]
    public void The_same_worktree_maps_to_the_same_marker_regardless_of_form()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "wt");
        var trailing = baseDir + Path.DirectorySeparatorChar;
        var upper = baseDir.ToUpperInvariant();

        Assert.Equal(
            OrchestrationPaths.ReadyMarker(baseDir), OrchestrationPaths.ReadyMarker(trailing));
        Assert.Equal(
            OrchestrationPaths.ReadyMarker(baseDir), OrchestrationPaths.ReadyMarker(upper));
    }

    [Fact]
    public void Different_worktrees_get_different_markers()
    {
        var a = OrchestrationPaths.ReadyMarker(Path.Combine(Path.GetTempPath(), "wt-a"));
        var b = OrchestrationPaths.ReadyMarker(Path.Combine(Path.GetTempPath(), "wt-b"));

        Assert.NotEqual(a, b);
    }
}
