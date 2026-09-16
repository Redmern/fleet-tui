using Fleet.Shared.Releases;

namespace Fleet.Tests.Shared;

public class VersionCompareTests
{
    [Theory]
    [InlineData("v0.2.0", "0.1.0", true)]
    [InlineData("0.2.0", "0.1.0", true)]
    [InlineData("v1.0.0", "1.0.0", false)]
    [InlineData("v0.1.0", "0.2.0", false)]
    public void Compares_semantic_versions_ignoring_a_leading_v(
        string latestTag, string current, bool expected)
    {
        Assert.Equal(expected, VersionCompare.IsNewer(latestTag, current));
    }

    [Fact]
    public void An_unparsable_tag_is_never_newer()
    {
        Assert.False(VersionCompare.IsNewer("not-a-version", "0.1.0"));
    }

    [Fact]
    public void An_unparsable_current_version_treats_any_valid_tag_as_newer()
    {
        Assert.True(VersionCompare.IsNewer("v0.1.0", "0.0.0-dev"));
    }
}
