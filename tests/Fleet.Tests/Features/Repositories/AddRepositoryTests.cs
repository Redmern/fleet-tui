using Fleet.Features.Repositories;
using Fleet.Features.Repositories.AddRepository;
using Fleet.Features.Repositories.AddRepository.Models;
using Fleet.Features.Repositories.ListRepositories;
using Fleet.Platform.Git;

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
    public async Task Creating_a_new_repo_produces_a_bare_repo_with_a_default_branch_worktree()
    {
        var result = await Handler().HandleAsync(
            AddRepositoryCommand.CreateNew(_root, "widgets", "main"));

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("widgets", result.Value.Name);

        var bare = Path.Combine(_root, "widgets");
        var worktree = Path.Combine(bare, "main");

        Assert.True(Directory.Exists(worktree));
        Assert.True(File.Exists(Path.Combine(worktree, ".git")));

        Assert.Equal("true", (await Git.RunAsync(bare, ["rev-parse", "--is-bare-repository"])).Out);
        Assert.Equal("main", (await Git.RunAsync(worktree, ["rev-parse", "--abbrev-ref", "HEAD"])).Out);
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
    public async Task Cloning_a_url_creates_the_default_branch_worktree()
    {
        await Handler().HandleAsync(AddRepositoryCommand.CreateNew(_root, "origin.git", "main"));
        var origin = Path.Combine(_root, "origin.git");

        var dest = Path.Combine(_root, "dest");
        Directory.CreateDirectory(dest);

        var result = await Handler().HandleAsync(
            AddRepositoryCommand.CloneFrom(dest, "widgets", origin, "main"));

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("main", result.Value.DefaultBranch);
        Assert.True(Directory.Exists(Path.Combine(dest, "widgets", "main")));
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