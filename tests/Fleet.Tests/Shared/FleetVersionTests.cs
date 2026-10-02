using Fleet.Shared.Constants;
using Fleet.Shared.Releases;

namespace Fleet.Tests.Shared;

public class FleetVersionTests
{
    [Theory]
    [InlineData("0.6.0.2", "0.6.0.2")]
    [InlineData("0.6.0.0", "0.6.0")]
    [InlineData("0.5.24.0", "0.5.24")]
    [InlineData("1.2.3", "1.2.3")]
    public void Keeps_the_fourth_part_only_when_it_is_set(string assemblyVersion, string expected)
    {
        Assert.Equal(expected, FleetVersion.Format(Version.Parse(assemblyVersion)));
    }

    [Fact]
    public void A_missing_version_formats_as_zero()
    {
        Assert.Equal("0.0.0", FleetVersion.Format(null));
    }

    [Theory]
    [InlineData("v0.6.0.2", "0.6.0.2")]
    [InlineData("v0.6.0", "0.6.0.0")]
    public void A_build_of_the_latest_release_is_not_offered_that_release_again(string tag, string assemblyVersion)
    {
        var current = FleetVersion.Format(Version.Parse(assemblyVersion));

        Assert.False(VersionCompare.IsNewer(tag, current));
        Assert.True(VersionCompare.AreEqual(tag, current));
    }
}
