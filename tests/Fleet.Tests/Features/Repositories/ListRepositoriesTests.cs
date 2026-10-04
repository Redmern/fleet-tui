using System.Collections.Concurrent;
using Fleet.Features.Repositories.ListRepositories;
using Fleet.Features.Repositories.ListRepositories.Enums;
using Fleet.Platform.Git;
using Fleet.Ports.Git;
using Fleet.Ports.Git.Models;

namespace Fleet.Tests.Features.Repositories;

public sealed class ListRepositoriesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fleet-tests", $"list-repos-{Guid.NewGuid():N}");

    public ListRepositoriesTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private string GitDir(string path, string? config, string head = "ref: refs/heads/main\n")
    {
        Directory.CreateDirectory(Path.Combine(path, "objects"));
        Directory.CreateDirectory(Path.Combine(path, "refs"));
        File.WriteAllText(Path.Combine(path, "HEAD"), head);

        if (config is not null)
        {
            File.WriteAllText(Path.Combine(path, "config"), config);
        }

        return path;
    }

    [Fact]
    public async Task Fleet_containers_plain_folders_and_checkouts_are_told_apart_without_git()
    {
        GitDir(Path.Combine(_root, "api", ".git"), "[core]\n\tbare = true\n", "ref: refs/heads/develop\n");
        GitDir(Path.Combine(_root, "web.git"), "[core]\n\tbare = true\n");
        GitDir(Path.Combine(_root, "checkout", ".git"), "[core]\n\tbare = false\n");
        Directory.CreateDirectory(Path.Combine(_root, ".fleet"));
        Directory.CreateDirectory(Path.Combine(_root, "notes"));
        Directory.CreateDirectory(Path.Combine(_root, "linked"));
        File.WriteAllText(Path.Combine(_root, "linked", ".git"), "gitdir: C:/elsewhere/.git/worktrees/linked\n");
        var git = new CountingGit();

        var repos = await new ListRepositoriesHandler(git).HandleAsync(_root);

        Assert.Equal(["api", "web.git"], repos.Select(r => r.Name));
        Assert.Equal(["develop", "main"], repos.Select(r => r.DefaultBranch));
        Assert.Empty(git.Asked);
    }

    [Fact]
    public async Task Only_a_folder_the_files_cannot_settle_is_asked_of_git()
    {
        GitDir(Path.Combine(_root, "api", ".git"), "[core]\n\tbare = true\n");
        GitDir(Path.Combine(_root, "odd"), "[core]\n\trepositoryformatversion = 0\n");
        GitDir(Path.Combine(_root, "half", ".git"), config: null);
        var git = new CountingGit { Bare = { Path.Combine(_root, "odd") } };

        var repos = await new ListRepositoriesHandler(git).HandleAsync(_root);

        Assert.Equal(["api", "odd"], repos.Select(r => r.Name));
        Assert.Equal("trunk", repos[1].DefaultBranch);
        Assert.Equal(
            [Path.Combine(_root, "half"), Path.Combine(_root, "odd")],
            git.Asked.Distinct().Order(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_files_git_itself_writes_are_read_the_way_git_reads_them()
    {
        var real = new GitRunner();
        await real.RunAsync(_root, ["init", "--bare", "--initial-branch=trunk", "store.git"]);
        await real.RunAsync(_root, ["init", "--initial-branch=main", "work"]);
        var git = new CountingGit();

        var repos = await new ListRepositoriesHandler(git).HandleAsync(_root);

        var repo = Assert.Single(repos);
        Assert.Equal(("store.git", "trunk"), (repo.Name, repo.DefaultBranch));
        Assert.Equal(FolderKind.Plain, RepositoryFolders.ProbeFolder(Path.Combine(_root, "work")).Kind);
        Assert.Empty(git.Asked);
    }

    private sealed class CountingGit : IGitRunner
    {
        private readonly ConcurrentQueue<string> _asked = new();

        public HashSet<string> Bare { get; } = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<string> Asked => [.. _asked];

        public Task<GitResult> RunAsync(string workDir, IReadOnlyList<string> args, string? stdin = null, CancellationToken ct = default)
        {
            _asked.Enqueue(workDir);

            return Task.FromResult(args[0] switch
            {
                "rev-parse" => new GitResult(0, Bare.Contains(workDir) ? "true" : "false", string.Empty),
                "symbolic-ref" => new GitResult(0, "trunk", string.Empty),
                _ => new GitResult(1, string.Empty, "unexpected"),
            });
        }
    }
}
