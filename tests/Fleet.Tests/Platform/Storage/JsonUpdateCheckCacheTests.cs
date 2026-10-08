using Fleet.Platform.Storage;
using Fleet.Ports.Releases.Models;

namespace Fleet.Tests.Platform.Storage;

[Collection(ConfigHomeCollection.Name)]
public sealed class JsonUpdateCheckCacheTests : ConfigHomeFixture
{
    [Fact]
    public void Nothing_is_cached_before_the_first_check() =>
        Assert.Null(new JsonUpdateCheckCache().Load());

    [Fact]
    public void A_saved_check_round_trips()
    {
        var checkedAt = new DateTimeOffset(2026, 10, 8, 12, 30, 0, TimeSpan.Zero);

        new JsonUpdateCheckCache().Save(new CachedUpdateCheck("v0.6.0.27", checkedAt, "owner/repo"));

        Assert.Equal(new CachedUpdateCheck("v0.6.0.27", checkedAt, "owner/repo"), new JsonUpdateCheckCache().Load());
    }

    [Fact]
    public void A_damaged_cache_file_reads_as_nothing_cached()
    {
        File.WriteAllText(FleetPaths.UpdateCheckFile, "{ not json");

        Assert.Null(new JsonUpdateCheckCache().Load());
    }
}
