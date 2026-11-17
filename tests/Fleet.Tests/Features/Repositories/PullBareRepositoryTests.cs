using Fleet.Features.Repositories.PullRepository;
using Fleet.Platform.Git;

namespace Fleet.Tests.Features.Repositories;

public sealed class PullBareRepositoryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "fleet-pull-bare-" + Guid.NewGuid().ToString("N"));

    private readonly GitRunner _git = new();

    public PullBareRepositoryTests() => Directory.CreateDirectory(_root);

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

    private async Task<string> RunAsync(string dir, params string[] args)
    {
        var result = await _git.RunAsync(
            dir, ["-c", "user.name=t", "-c", "user.email=t@t", .. args]);

        Assert.True(result.Ok, $"git {string.Join(' ', args)}: {result.Message}");

        return result.Out;
    }

    private async Task CommitAsync(string clone, string message)
    {
        await RunAsync(clone, "commit", "--allow-empty", "-m", message);
        await RunAsync(clone, "push", "origin", "HEAD:main");
    }

    private async Task<(string Upstream, string Container)> LayoutAsync()
    {
        var upstream = Path.Combine(_root, "upstream.git");
        var seed = Path.Combine(_root, "seed");
        var container = Path.Combine(_root, "repo");

        await RunAsync(_root, "init", "--bare", "-b", "main", upstream);
        await RunAsync(_root, "clone", upstream, seed);
        await RunAsync(seed, "switch", "-c", "main");
        await CommitAsync(seed, "first");

        await RunAsync(_root, "init", "--bare", "-b", "main", container);
        await RunAsync(container, "remote", "add", "origin", upstream);
        await RunAsync(container, "config", "remote.origin.fetch", "+refs/heads/*:refs/remotes/origin/*");
        await RunAsync(container, "fetch", "origin");
        await RunAsync(container, "branch", "main", "origin/main");

        return (upstream, container);
    }

    private async Task<string> AdvanceUpstreamAsync(string message)
    {
        var seed = Path.Combine(_root, "seed");
        await CommitAsync(seed, message);

        return await RunAsync(seed, "rev-parse", "HEAD");
    }

    private PullRepositoryHandler Handler() => new(_git);

    [Fact]
    public async Task A_stale_local_default_is_moved_to_origin_and_the_move_is_reported()
    {
        var (_, container) = await LayoutAsync();
        var before = await RunAsync(container, "rev-parse", "refs/heads/main");
        await AdvanceUpstreamAsync("second");
        var latest = await AdvanceUpstreamAsync("third");

        var result = await Handler().HandleAsync(container, "main");

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(latest, await RunAsync(container, "rev-parse", "refs/heads/main"));
        Assert.NotEqual(before, await RunAsync(container, "rev-parse", "refs/heads/main"));
        Assert.Contains("moved main", result.Value);
        Assert.Contains(before[..7], result.Value);
        Assert.Contains("2 commits", result.Value);
    }

    [Fact]
    public async Task One_new_commit_is_reported_in_the_singular()
    {
        var (_, container) = await LayoutAsync();
        var tip = await AdvanceUpstreamAsync("second");

        var result = await Handler().HandleAsync(container, "main");

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(tip, await RunAsync(container, "rev-parse", "refs/heads/main"));
        Assert.Contains("1 commit", result.Value);
        Assert.DoesNotContain("1 commits", result.Value);
    }

    [Fact]
    public async Task A_local_default_that_is_already_current_says_so_and_moves_nothing()
    {
        var (_, container) = await LayoutAsync();
        var before = await RunAsync(container, "rev-parse", "refs/heads/main");

        var result = await Handler().HandleAsync(container, "main");

        Assert.True(result.Succeeded, result.Error);
        Assert.Contains("already up to date", result.Value);
        Assert.Equal(before, await RunAsync(container, "rev-parse", "refs/heads/main"));
    }

    [Fact]
    public async Task A_local_default_that_diverged_is_left_alone_and_the_result_says_why()
    {
        var (_, container) = await LayoutAsync();
        var scratch = Path.Combine(_root, "scratch");
        await RunAsync(_root, "clone", container, scratch);
        await RunAsync(scratch, "switch", "main");
        await RunAsync(scratch, "commit", "--allow-empty", "-m", "local only");
        await RunAsync(scratch, "push", "origin", "main");
        var diverged = await RunAsync(container, "rev-parse", "refs/heads/main");
        await AdvanceUpstreamAsync("upstream only");

        var result = await Handler().HandleAsync(container, "main");

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(diverged, await RunAsync(container, "rev-parse", "refs/heads/main"));
        Assert.Contains("not a fast-forward", result.Value);
        Assert.Contains("main left as is", result.Value);
    }

    [Fact]
    public async Task A_local_default_ahead_of_origin_is_left_alone_and_the_result_says_why()
    {
        var (_, container) = await LayoutAsync();
        var scratch = Path.Combine(_root, "scratch");
        await RunAsync(_root, "clone", container, scratch);
        await RunAsync(scratch, "switch", "main");
        await RunAsync(scratch, "commit", "--allow-empty", "-m", "local only");
        await RunAsync(scratch, "push", "origin", "main");
        var ahead = await RunAsync(container, "rev-parse", "refs/heads/main");

        var result = await Handler().HandleAsync(container, "main");

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(ahead, await RunAsync(container, "rev-parse", "refs/heads/main"));
        Assert.Contains("not a fast-forward", result.Value);
    }

    [Fact]
    public async Task A_missing_local_default_is_created_at_origin()
    {
        var (_, container) = await LayoutAsync();
        await RunAsync(container, "update-ref", "-d", "refs/heads/main");
        await RunAsync(container, "symbolic-ref", "HEAD", "refs/heads/elsewhere");

        var result = await Handler().HandleAsync(container, "main");

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(
            await RunAsync(container, "rev-parse", "refs/remotes/origin/main"),
            await RunAsync(container, "rev-parse", "refs/heads/main"));
        Assert.Contains("created main", result.Value);
    }
}
