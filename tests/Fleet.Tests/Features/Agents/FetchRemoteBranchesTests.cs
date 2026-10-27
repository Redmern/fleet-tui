using Fleet.Features.Agents.NewAgent;
using Fleet.Ports.Git;
using Fleet.Ports.Git.Models;

namespace Fleet.Tests.Features.Agents;

public class FetchRemoteBranchesTests
{
    private sealed class ScriptedGit(Func<IReadOnlyList<string>, CancellationToken, Task<GitResult>> answer) : IGitRunner
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];

        public Task<GitResult> RunAsync(
            string workDir, IReadOnlyList<string> args, string? stdin = null, CancellationToken ct = default)
        {
            Calls.Add(args);
            return answer(args, ct);
        }
    }

    private static readonly GitResult Refs =
        new(0, "refs/heads/main\nrefs/remotes/origin/main\nrefs/remotes/origin/feat/new\n", string.Empty);

    private static Task<GitResult> Ok(IReadOnlyList<string> args) =>
        Task.FromResult(args[0] == "fetch" ? new GitResult(0, string.Empty, string.Empty) : Refs);

    [Fact]
    public async Task It_prunes_and_fetches_origin_before_listing()
    {
        var git = new ScriptedGit((args, _) => Ok(args));

        await new FetchRemoteBranchesHandler(git).HandleAsync("/r/backend");

        Assert.Equal(["fetch", "--prune", "origin"], git.Calls[0]);
        Assert.Equal("for-each-ref", git.Calls[1][0]);
    }

    [Fact]
    public async Task A_successful_fetch_lists_the_new_remote_branches()
    {
        var git = new ScriptedGit((args, _) => Ok(args));

        var fetched = await new FetchRemoteBranchesHandler(git).HandleAsync("/r/backend");

        Assert.True(fetched.Succeeded, fetched.Error);
        Assert.Equal(["main", "origin/feat/new"], fetched.Value.Select(b => b.Reference));
    }

    [Fact]
    public async Task A_failed_fetch_says_it_is_offline_and_lists_nothing()
    {
        var git = new ScriptedGit((_, _) => Task.FromResult(new GitResult(128, string.Empty, "Could not resolve host")));

        var fetched = await new FetchRemoteBranchesHandler(git).HandleAsync("/r/backend");

        Assert.False(fetched.Succeeded);
        Assert.Contains("offline", fetched.Error);
        Assert.Single(git.Calls);
    }

    [Fact]
    public async Task A_fetch_that_hangs_times_out_as_offline()
    {
        var git = new ScriptedGit(async (args, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Refs;
        });

        var fetched = await new FetchRemoteBranchesHandler(git, TimeSpan.FromMilliseconds(50)).HandleAsync("/r/backend");

        Assert.False(fetched.Succeeded);
        Assert.Contains("timed out", fetched.Error);
    }

    [Fact]
    public async Task Closing_the_picker_cancels_the_fetch_instead_of_reporting_offline()
    {
        using var closed = new CancellationTokenSource();
        var git = new ScriptedGit(async (args, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Refs;
        });

        var fetching = new FetchRemoteBranchesHandler(git).HandleAsync("/r/backend", closed.Token);
        await closed.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetching);
    }

    [Fact]
    public void The_default_timeout_is_fifteen_seconds() =>
        Assert.Equal(TimeSpan.FromSeconds(15), FetchRemoteBranchesHandler.DefaultTimeout);

    // list_branches (MCP) goes through ListBranchesHandler; it must stay a cheap local read.
    [Fact]
    public async Task Plain_branch_listing_never_fetches()
    {
        var git = new ScriptedGit((args, _) => Ok(args));

        await new ListBranchesHandler(git).HandleAsync("/r/backend");

        Assert.DoesNotContain(git.Calls, c => c[0] == "fetch");
    }
}
