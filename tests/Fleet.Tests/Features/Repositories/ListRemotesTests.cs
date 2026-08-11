using Fleet.Features.Repositories.ListRemotes;
using Fleet.Ports.Git;
using Fleet.Ports.Git.Models;

namespace Fleet.Tests.Features.Repositories;

public class ListRemotesTests
{
    [Fact]
    public async Task Each_repository_contributes_the_url_it_was_cloned_from()
    {
        var git = new StubGit(new Dictionary<string, string>
        {
            ["a"] = "https://dev.azure.com/org/project/_git/backend",
            ["b"] = "https://dev.azure.com/org/project/_git/frontend",
        });

        var urls = await new ListRemotesHandler(git).HandleAsync(["a", "b"]);

        Assert.Equal(
            [
                "https://dev.azure.com/org/project/_git/backend",
                "https://dev.azure.com/org/project/_git/frontend",
            ],
            urls);
    }

    [Fact]
    public async Task A_repository_without_a_remote_is_skipped_rather_than_listed_blank()
    {
        var git = new StubGit(new Dictionary<string, string> { ["b"] = "git@host:team/x.git" });

        Assert.Equal(["git@host:team/x.git"], await new ListRemotesHandler(git).HandleAsync(["a", "b"]));
    }

    [Fact]
    public async Task The_same_url_twice_is_offered_once()
    {
        var git = new StubGit(new Dictionary<string, string>
        {
            ["a"] = "https://host/x.git",
            ["b"] = "https://host/x.git",
        });

        Assert.Single(await new ListRemotesHandler(git).HandleAsync(["a", "b"]));
    }

    private sealed class StubGit(Dictionary<string, string> urls) : IGitRunner
    {
        public Task<GitResult> RunAsync(
            string workDir,
            IReadOnlyList<string> args,
            string? stdin = null,
            CancellationToken ct = default) =>
            Task.FromResult(urls.TryGetValue(workDir, out var url)
                ? new GitResult(0, url + "\n", string.Empty)
                : new GitResult(1, string.Empty, "no remote"));
    }
}
