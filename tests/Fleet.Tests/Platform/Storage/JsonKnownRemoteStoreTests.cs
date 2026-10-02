using Fleet.Platform.Storage;

namespace Fleet.Tests.Platform.Storage;

[Collection(ConfigHomeCollection.Name)]
public sealed class JsonKnownRemoteStoreTests : ConfigHomeFixture
{
    private static readonly DateTimeOffset Monday = new(2026, 9, 28, 9, 0, 0, TimeSpan.Zero);

    private static JsonKnownRemoteStore Store => new();

    [Fact]
    public void Load_is_empty_when_nothing_is_saved()
    {
        Assert.Empty(Store.Load());
    }

    [Fact]
    public void Remember_rename_then_Load_round_trips()
    {
        Store.Remember("user@homelab", Monday);
        Store.Rename("user@homelab", "  homelab  ");

        var remote = Assert.Single(Store.Load());
        Assert.Equal("user@homelab", remote.Host);
        Assert.Equal("homelab", remote.Nickname);
        Assert.Equal(Monday, remote.LastConnected);
    }

    [Fact]
    public void Remember_again_updates_the_time_and_keeps_the_nickname()
    {
        Store.Remember("user@homelab", Monday);
        Store.Rename("user@homelab", "homelab");
        Store.Remember("USER@HOMELAB", Monday.AddDays(1));

        var remote = Assert.Single(Store.Load());
        Assert.Equal("user@homelab", remote.Host);
        Assert.Equal("homelab", remote.Nickname);
        Assert.Equal(Monday.AddDays(1), remote.LastConnected);
    }

    [Fact]
    public void Hosts_match_case_insensitively_for_rename_and_forget()
    {
        Store.Remember("user@homelab", Monday);
        Store.Remember("pi@garage", Monday);

        Store.Rename("User@HomeLab", "homelab");
        Store.Forget("PI@GARAGE");

        var remote = Assert.Single(Store.Load());
        Assert.Equal("homelab", remote.Nickname);
    }

    [Fact]
    public void An_empty_nickname_clears_it()
    {
        Store.Remember("user@homelab", Monday);
        Store.Rename("user@homelab", "homelab");
        Store.Rename("user@homelab", " ");

        var remote = Assert.Single(Store.Load());
        Assert.Null(remote.Nickname);
        Assert.Equal("user@homelab", remote.Label);
    }

    [Fact]
    public void Rename_of_an_unknown_host_remembers_nothing()
    {
        Store.Rename("user@homelab", "homelab");

        Assert.Empty(Store.Load());
    }

    [Fact]
    public void A_corrupt_file_loads_as_empty_and_is_replaced_on_the_next_save()
    {
        FleetPaths.EnsureDirs();
        File.WriteAllText(FleetPaths.KnownRemotesFile, "{ not json");

        Assert.Empty(Store.Load());

        Store.Remember("user@homelab", Monday);
        Assert.Single(Store.Load());
    }

    [Fact]
    public void Duplicate_hosts_in_the_file_load_once()
    {
        FleetPaths.EnsureDirs();
        File.WriteAllText(
            FleetPaths.KnownRemotesFile,
            """{"version":1,"remotes":[{"host":"user@homelab","nickname":"homelab"},{"host":"USER@homelab"},{"host":""}]}""");

        var remote = Assert.Single(Store.Load());
        Assert.Equal("homelab", remote.Nickname);
    }
}
