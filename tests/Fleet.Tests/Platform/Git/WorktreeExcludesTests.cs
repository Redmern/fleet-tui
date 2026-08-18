using Fleet.Platform.Git;
using Fleet.Ports.Git;
using Fleet.Ports.Git.Models;

namespace Fleet.Tests.Platform.Git;

public sealed class WorktreeExcludesTests : IDisposable
{
    private readonly string _worktree =
        Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    public WorktreeExcludesTests() => Directory.CreateDirectory(_worktree);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_worktree, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private string Exclude => Path.Combine(_worktree, ".git", "info", "exclude");

    [Fact]
    public async Task It_writes_the_patterns_into_the_common_dir_exclude()
    {
        await WorktreeExcludes.EnsureLocallyExcludedAsync(
            new StubGit(".git"), _worktree, ["/.mcp.json", "/.fleet-ready"]);

        var lines = await File.ReadAllLinesAsync(Exclude);

        Assert.Contains("/.mcp.json", lines);
        Assert.Contains("/.fleet-ready", lines);
        Assert.Contains("# fleet", lines);
    }

    [Fact]
    public async Task It_is_idempotent()
    {
        await WorktreeExcludes.EnsureLocallyExcludedAsync(new StubGit(".git"), _worktree, ["/.mcp.json"]);
        await WorktreeExcludes.EnsureLocallyExcludedAsync(new StubGit(".git"), _worktree, ["/.mcp.json"]);

        var lines = await File.ReadAllLinesAsync(Exclude);

        Assert.Single(lines, l => l == "/.mcp.json");
        Assert.Single(lines, l => l == "# fleet");
    }

    [Fact]
    public async Task It_preserves_existing_exclude_content()
    {
        var info = Path.Combine(_worktree, ".git", "info");
        Directory.CreateDirectory(info);
        await File.WriteAllTextAsync(Path.Combine(info, "exclude"), "*.log\n");

        await WorktreeExcludes.EnsureLocallyExcludedAsync(new StubGit(".git"), _worktree, ["/.mcp.json"]);

        var lines = await File.ReadAllLinesAsync(Exclude);

        Assert.Contains("*.log", lines);
        Assert.Contains("/.mcp.json", lines);
    }

    [Fact]
    public async Task It_does_nothing_when_the_git_dir_cannot_be_resolved()
    {
        await WorktreeExcludes.EnsureLocallyExcludedAsync(new StubGit(null), _worktree, ["/.mcp.json"]);

        Assert.False(File.Exists(Exclude));
    }

    private sealed class StubGit(string? commonDir) : IGitRunner
    {
        public Task<GitResult> RunAsync(
            string workDir,
            IReadOnlyList<string> args,
            string? stdin = null,
            CancellationToken ct = default) =>
            Task.FromResult(commonDir is null
                ? new GitResult(1, string.Empty, "not a repo")
                : new GitResult(0, commonDir + "\n", string.Empty));
    }
}
