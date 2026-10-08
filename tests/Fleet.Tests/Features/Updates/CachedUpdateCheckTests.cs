using Fleet.Features.Updates.CheckUpdate;
using Fleet.Platform.Releases.Fake;
using Fleet.Ports.Releases;
using Fleet.Ports.Releases.Models;

namespace Fleet.Tests.Features.Updates;

public class CachedUpdateCheckTests
{
    private const string Repo = "owner/repo";

    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    private sealed class MemoryCache : IUpdateCheckCache
    {
        public CachedUpdateCheck? Saved { get; set; }

        public CachedUpdateCheck? Load() => Saved;

        public void Save(CachedUpdateCheck check) => Saved = check;
    }

    private sealed class CountingClient(ReleaseInfo? release) : IReleaseClient
    {
        private readonly FakeReleaseClient _inner = new() { Release = release };

        public int Lookups { get; private set; }

        public Task<ReleaseInfo?> LatestAsync(string repo, CancellationToken ct = default)
        {
            Lookups++;
            return _inner.LatestAsync(repo, ct);
        }

        public Task<ReleaseInfo?> ForVersionAsync(string repo, string version, CancellationToken ct = default) =>
            _inner.ForVersionAsync(repo, version, ct);

        public Task<IReadOnlyList<ReleaseInfo>> ListAsync(string repo, CancellationToken ct = default) =>
            _inner.ListAsync(repo, ct);

        public Task<byte[]?> DownloadAsync(string url, CancellationToken ct = default) =>
            _inner.DownloadAsync(url, ct);
    }

    [Fact]
    public async Task A_check_within_the_hour_answers_from_the_cache_without_asking_github()
    {
        var client = new CountingClient(new ReleaseInfo("v0.3.0", []));
        var cache = new MemoryCache { Saved = new CachedUpdateCheck("v0.2.0", Now.AddMinutes(-59), Repo) };

        var check = await new CheckUpdateHandler(client, cache, () => Now).HandleCachedAsync(Repo, "0.1.0");

        Assert.Equal(0, client.Lookups);
        Assert.Equal("v0.2.0", check.Latest);
        Assert.True(check.UpdateAvailable);
    }

    [Fact]
    public async Task A_check_after_the_hour_asks_github_and_remembers_the_answer()
    {
        var client = new CountingClient(new ReleaseInfo("v0.3.0", []));
        var cache = new MemoryCache { Saved = new CachedUpdateCheck("v0.2.0", Now.AddHours(-2), Repo) };

        var check = await new CheckUpdateHandler(client, cache, () => Now).HandleCachedAsync(Repo, "0.1.0");

        Assert.Equal(1, client.Lookups);
        Assert.Equal("v0.3.0", check.Latest);
        Assert.Equal(new CachedUpdateCheck("v0.3.0", Now, Repo), cache.Saved);
    }

    [Fact]
    public async Task Offline_the_last_known_answer_is_used_however_old()
    {
        var client = new CountingClient(null);
        var cache = new MemoryCache { Saved = new CachedUpdateCheck("v0.2.0", Now.AddDays(-3), Repo) };

        var check = await new CheckUpdateHandler(client, cache, () => Now).HandleCachedAsync(Repo, "0.1.0");

        Assert.Null(check.Error);
        Assert.Equal("v0.2.0", check.Latest);
        Assert.True(check.UpdateAvailable);
    }

    [Fact]
    public async Task Offline_with_nothing_cached_reports_the_error()
    {
        var check = await new CheckUpdateHandler(new CountingClient(null), new MemoryCache(), () => Now)
            .HandleCachedAsync(Repo, "0.1.0");

        Assert.False(check.UpdateAvailable);
        Assert.NotNull(check.Error);
    }

    [Fact]
    public async Task A_check_cached_for_another_repository_is_ignored()
    {
        var client = new CountingClient(null);
        var cache = new MemoryCache { Saved = new CachedUpdateCheck("v9.0.0", Now, "someone/fork") };
        var handler = new CheckUpdateHandler(client, cache, () => Now);

        var check = await handler.HandleCachedAsync(Repo, "0.1.0");

        Assert.Equal(1, client.Lookups);
        Assert.NotNull(check.Error);
        Assert.Null(handler.Cached(Repo, "0.1.0"));
    }

    [Fact]
    public async Task A_cache_stamped_in_the_future_is_not_trusted()
    {
        var client = new CountingClient(new ReleaseInfo("v0.3.0", []));
        var cache = new MemoryCache { Saved = new CachedUpdateCheck("v0.2.0", Now.AddHours(5), Repo) };

        await new CheckUpdateHandler(client, cache, () => Now).HandleCachedAsync(Repo, "0.1.0");

        Assert.Equal(1, client.Lookups);
    }

    [Fact]
    public void The_cached_answer_reads_without_any_network()
    {
        var client = new CountingClient(new ReleaseInfo("v0.3.0", []));
        var handler = new CheckUpdateHandler(client, new MemoryCache { Saved = new CachedUpdateCheck("v0.1.0", Now, Repo) });

        var check = handler.Cached(Repo, "0.1.0");

        Assert.Equal(0, client.Lookups);
        Assert.NotNull(check);
        Assert.False(check.UpdateAvailable);
        Assert.Null(new CheckUpdateHandler(client, new MemoryCache()).Cached(Repo, "0.1.0"));
    }

    [Fact]
    public async Task The_notice_names_the_installed_and_the_available_version()
    {
        var check = await new CheckUpdateHandler(new CountingClient(new ReleaseInfo("v0.6.0.27", [])))
            .HandleAsync(Repo, "0.6.0.26");

        Assert.Equal("You are on v0.6.0.26. v0.6.0.27 is available.", check.Notice("0.6.0.26"));
    }

    [Fact]
    public async Task There_is_no_notice_when_up_to_date()
    {
        var check = await new CheckUpdateHandler(new CountingClient(new ReleaseInfo("v0.6.0.26", [])))
            .HandleAsync(Repo, "0.6.0.26");

        Assert.Null(check.Notice("0.6.0.26"));
    }
}
