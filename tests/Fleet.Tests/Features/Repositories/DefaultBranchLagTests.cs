using Fleet.Features.Repositories.PullRepository;
using Fleet.Platform.Git;

namespace Fleet.Tests.Features.Repositories;

public sealed class DefaultBranchLagTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "fleet-lag-" + Guid.NewGuid().ToString("N"));

    private readonly GitRunner _git = new();

    public DefaultBranchLagTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private async Task RunAsync(string dir, params string[] args)
    {
        var result = await _git.RunAsync(
            dir, ["-c", "user.name=t", "-c", "user.email=t@t", .. args]);

        Assert.True(result.Ok, $"git {string.Join(' ', args)}: {result.Message}");
    }

    private async Task<string> ContainerAsync()
    {
        var upstream = Path.Combine(_root, "upstream.git");
        var seed = Path.Combine(_root, "seed");
        var container = Path.Combine(_root, "repo");

        await RunAsync(_root, "init", "--bare", "-b", "main", upstream);
        await RunAsync(_root, "clone", upstream, seed);
        await RunAsync(seed, "switch", "-c", "main");
        await RunAsync(seed, "commit", "--allow-empty", "-m", "first");
        await RunAsync(seed, "push", "origin", "main");

        await RunAsync(_root, "init", "--bare", "-b", "main", container);
        await RunAsync(container, "remote", "add", "origin", upstream);
        await RunAsync(container, "config", "remote.origin.fetch", "+refs/heads/*:refs/remotes/origin/*");
        await RunAsync(container, "fetch", "origin");
        await RunAsync(container, "branch", "main", "origin/main");

        return container;
    }

    private async Task UpstreamCommitsAsync(int count)
    {
        var seed = Path.Combine(_root, "seed");

        for (var i = 0; i < count; i++)
        {
            await RunAsync(seed, "commit", "--allow-empty", "-m", $"upstream {i}");
        }

        await RunAsync(seed, "push", "origin", "main");
    }

    private DefaultBranchLag Lag() => new(_git);

    [Fact]
    public async Task A_local_default_behind_origin_names_how_far_in_both_directions()
    {
        var container = await ContainerAsync();
        await UpstreamCommitsAsync(3);
        await RunAsync(container, "fetch", "origin");

        var text = await Lag().DescribeAsync(container, "main");

        Assert.Equal("local main is 3 behind and 0 ahead of origin/main", text);
    }

    [Fact]
    public async Task A_local_default_level_with_origin_says_so()
    {
        var container = await ContainerAsync();

        var text = await Lag().DescribeAsync(container, "main");

        Assert.Equal("local main is level with origin/main", text);
    }

    [Fact]
    public async Task A_diverged_local_default_counts_both_sides()
    {
        var container = await ContainerAsync();
        var scratch = Path.Combine(_root, "scratch");
        await RunAsync(_root, "clone", container, scratch);
        await RunAsync(scratch, "switch", "main");
        await RunAsync(scratch, "commit", "--allow-empty", "-m", "local only");
        await RunAsync(scratch, "push", "origin", "main");
        await UpstreamCommitsAsync(2);
        await RunAsync(container, "fetch", "origin");

        var text = await Lag().DescribeAsync(container, "main");

        Assert.Equal("local main is 2 behind and 1 ahead of origin/main", text);
    }

    [Fact]
    public async Task A_default_with_no_origin_counterpart_says_it_cannot_compare()
    {
        var container = await ContainerAsync();
        await RunAsync(container, "update-ref", "-d", "refs/remotes/origin/main");

        var text = await Lag().DescribeAsync(container, "main");

        Assert.Equal("local main cannot be compared with origin/main (fetch first)", text);
    }

    [Fact]
    public async Task Not_a_repository_says_it_cannot_compare()
    {
        var text = await Lag().DescribeAsync(_root, "main");

        Assert.Contains("cannot be compared", text);
    }
}
