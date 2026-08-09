using Fleet.Features.Repositories.AddRepository;
using Fleet.Features.Repositories.AddRepository.Models;
using Fleet.Features.Repositories.ListRepositories;
using Fleet.Platform.Git;
using Fleet.Shared;

namespace Fleet.Tests.Features.Repositories;

public sealed class AddRepositoryTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    public AddRepositoryTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static AddRepositoryHandler Handler() => new(new GitRunner());

    private static GitRunner Git => new();

    [Fact]
    public async Task A_new_repo_puts_the_bare_clone_in_dot_git_beside_the_worktree()
    {
        var result = await Handler().HandleAsync(
            AddRepositoryCommand.CreateNew(_root, "widgets", "main"));

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("widgets", result.Value.Name);

        var container = Path.Combine(_root, "widgets");
        var worktree = Path.Combine(container, "main");

        Assert.True(Directory.Exists(Path.Combine(container, ".git")));
        Assert.True(Directory.Exists(worktree));
        Assert.True(File.Exists(Path.Combine(worktree, ".git")));
        Assert.Equal(container, result.Value.Path);

        Assert.Equal(
            "true",
            (await Git.RunAsync(container, ["rev-parse", "--is-bare-repository"])).Out);

        Assert.Equal(
            "main",
            (await Git.RunAsync(worktree, ["rev-parse", "--abbrev-ref", "HEAD"])).Out);
    }

    [Fact]
    public async Task Git_internals_do_not_sit_beside_the_worktrees()
    {
        await Handler().HandleAsync(AddRepositoryCommand.CreateNew(_root, "widgets", "main"));

        var entries = Directory.GetFileSystemEntries(Path.Combine(_root, "widgets"))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Equal(2, entries.Count);
        Assert.Contains(".git", entries);
        Assert.Contains("main", entries);
        Assert.DoesNotContain("objects", entries);
        Assert.DoesNotContain("refs", entries);
        Assert.DoesNotContain("packed-refs", entries);
    }

    [Fact]
    public async Task The_seeded_repository_has_exactly_one_empty_commit()
    {
        await Handler().HandleAsync(AddRepositoryCommand.CreateNew(_root, "widgets", "main"));
        var worktree = Path.Combine(_root, "widgets", "main");

        Assert.Equal("1", (await Git.RunAsync(worktree, ["rev-list", "--count", "HEAD"])).Out);
        Assert.Empty((await Git.RunAsync(worktree, ["ls-files"])).Out);
    }

    [Fact]
    public async Task A_branch_with_a_slash_is_slugged_into_the_directory_name()
    {
        var result = await Handler().HandleAsync(
            AddRepositoryCommand.CreateNew(_root, "widgets", "release/v1"));

        Assert.True(result.Succeeded, result.Error);
        Assert.True(Directory.Exists(Path.Combine(_root, "widgets", "release_v1")));
        Assert.Equal("release_v1", BranchSlug.Of("release/v1"));
    }

    [Fact]
    public async Task An_existing_name_is_refused()
    {
        await Handler().HandleAsync(AddRepositoryCommand.CreateNew(_root, "widgets", "main"));

        var again = await Handler().HandleAsync(
            AddRepositoryCommand.CreateNew(_root, "widgets", "main"));

        Assert.False(again.Succeeded);
        Assert.Contains("already exists", again.Error);
    }

    [Fact]
    public async Task Cloning_a_url_produces_the_same_layout()
    {
        await Handler().HandleAsync(AddRepositoryCommand.CreateNew(_root, "origin", "develop"));
        var origin = Path.Combine(_root, "origin");

        var dest = Path.Combine(_root, "dest");
        Directory.CreateDirectory(dest);

        var result = await Handler().HandleAsync(
            AddRepositoryCommand.CloneFrom(dest, "backend", origin, "develop"));

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("develop", result.Value.DefaultBranch);
        Assert.True(Directory.Exists(Path.Combine(dest, "backend", ".git")));
        Assert.True(Directory.Exists(Path.Combine(dest, "backend", "develop")));
    }

    [Fact]
    public async Task Cloning_records_the_requested_branch_as_the_repository_head()
    {
        await Handler().HandleAsync(AddRepositoryCommand.CreateNew(_root, "origin", "main"));
        var origin = Path.Combine(_root, "origin");

        await Git.RunAsync(Path.Combine(origin, "main"), ["branch", "develop"]);

        var dest = Path.Combine(_root, "dest");
        Directory.CreateDirectory(dest);

        var result = await Handler().HandleAsync(
            AddRepositoryCommand.CloneFrom(dest, "backend", origin, "develop"));

        Assert.True(result.Succeeded, result.Error);

        var container = Path.Combine(dest, "backend");

        Assert.Equal(
            "develop",
            (await Git.RunAsync(container, ["symbolic-ref", "--short", "HEAD"])).Out);

        Assert.Equal("develop", result.Value.DefaultBranch);
    }

    [Fact]
    public async Task A_cloned_repository_lists_with_the_branch_that_was_asked_for()
    {
        await Handler().HandleAsync(AddRepositoryCommand.CreateNew(_root, "origin", "main"));
        await Git.RunAsync(Path.Combine(_root, "origin", "main"), ["branch", "develop"]);

        var dest = Path.Combine(_root, "dest");
        Directory.CreateDirectory(dest);

        await Handler().HandleAsync(
            AddRepositoryCommand.CloneFrom(dest, "backend", Path.Combine(_root, "origin"), "develop"));

        var repos = await new ListRepositoriesHandler(new GitRunner()).HandleAsync(dest);

        Assert.Equal("develop", Assert.Single(repos).DefaultBranch);
    }

    [Fact]
    public async Task Cloning_with_a_branch_the_remote_lacks_fails_and_leaves_nothing_behind()
    {
        await Handler().HandleAsync(AddRepositoryCommand.CreateNew(_root, "origin", "main"));

        var dest = Path.Combine(_root, "dest");
        Directory.CreateDirectory(dest);

        var result = await Handler().HandleAsync(
            AddRepositoryCommand.CloneFrom(dest, "backend", Path.Combine(_root, "origin"), "nope"));

        Assert.False(result.Succeeded);
        Assert.Contains("no branch named 'nope'", result.Error);
        Assert.False(Directory.Exists(Path.Combine(dest, "backend")));
    }

    [Fact]
    public async Task Cloning_a_bad_url_fails_with_gits_own_message()
    {
        var result = await Handler().HandleAsync(
            AddRepositoryCommand.CloneFrom(_root, "widgets", Path.Combine(_root, "nope"), "main"));

        Assert.False(result.Succeeded);
        Assert.Contains("git clone", result.Error);
    }

    [Fact]
    public async Task A_blank_name_is_refused_without_touching_the_disk()
    {
        var result = await Handler().HandleAsync(
            AddRepositoryCommand.CreateNew(_root, "  ", "main"));

        Assert.False(result.Succeeded);
        Assert.Empty(Directory.EnumerateDirectories(_root));
    }

    [Fact]
    public async Task A_blank_branch_is_refused()
    {
        var result = await Handler().HandleAsync(
            AddRepositoryCommand.CreateNew(_root, "widgets", "   "));

        Assert.False(result.Succeeded);
        Assert.Contains("default branch", result.Error);
    }

    [Fact]
    public async Task A_clone_without_a_url_is_refused()
    {
        var result = await Handler().HandleAsync(
            AddRepositoryCommand.CloneFrom(_root, "widgets", string.Empty, "main"));

        Assert.False(result.Succeeded);
        Assert.Contains("URL", result.Error);
    }

    [Fact]
    public async Task ListRepositories_finds_bare_containers_and_ignores_plain_directories()
    {
        await Handler().HandleAsync(AddRepositoryCommand.CreateNew(_root, "widgets", "main"));
        Directory.CreateDirectory(Path.Combine(_root, "not-a-repo"));

        var repos = await new ListRepositoriesHandler(new GitRunner()).HandleAsync(_root);

        var repo = Assert.Single(repos);
        Assert.Equal("widgets", repo.Name);
        Assert.Equal("main", repo.DefaultBranch);
    }

    [Fact]
    public async Task ListRepositories_returns_empty_for_a_root_that_does_not_exist()
        => Assert.Empty(await new ListRepositoriesHandler(new GitRunner())
            .HandleAsync(Path.Combine(_root, "nope")));

    [Fact]
    public async Task ListRepositories_sorts_by_name()
    {
        await Handler().HandleAsync(AddRepositoryCommand.CreateNew(_root, "zeta", "main"));
        await Handler().HandleAsync(AddRepositoryCommand.CreateNew(_root, "alpha", "main"));

        var repos = await new ListRepositoriesHandler(new GitRunner()).HandleAsync(_root);

        Assert.Equal(["alpha", "zeta"], repos.Select(r => r.Name));
    }
}