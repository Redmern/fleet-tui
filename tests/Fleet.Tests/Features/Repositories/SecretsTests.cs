using Fleet.Features.Repositories;
using Fleet.Features.Repositories.Secrets;
using Fleet.Shared;
using Fleet.Ui;

namespace Fleet.Tests.Features.Repositories;

public sealed class SecretsTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    public SecretsTests() => Directory.CreateDirectory(_root);

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

    private string Project => Path.Combine(_root, "techweb");

    private string Container => Path.Combine(Project, "backend");

    private string Secret(string relative)
    {
        var path = Path.Combine(SecretsMirror.Root(Project, "backend", "develop"), relative);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "value");

        return path;
    }

    private string Worktree(string branch)
    {
        var path = Path.Combine(Container, branch);

        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, ".git"), "gitdir: ../.git/worktrees/" + branch);

        return path;
    }

    [Fact]
    public void The_mirror_lives_under_the_project_config_folder()
    {
        Assert.Equal(
            Path.Combine(Project, ".config", "backend", "develop"),
            SecretsMirror.Root(Project, "backend", "develop"));
    }

    [Fact]
    public void A_branch_with_a_slash_uses_the_same_slug_the_worktrees_use()
    {
        Assert.EndsWith(
            Path.Combine("backend", "release_1.2"),
            SecretsMirror.Root(Project, "backend", "release/1.2"));
    }

    [Fact]
    public void Files_keep_the_place_they_hold_inside_the_repository()
    {
        Secret(".env");
        Secret(Path.Combine("src", "Api", "appsettings.Local.json"));

        var files = SecretsMirror.Files(SecretsMirror.Root(Project, "backend", "develop"));

        Assert.Equal([".env", Path.Combine("src", "Api", "appsettings.Local.json")], files);
    }

    [Fact]
    public void Copying_recreates_the_folders_inside_the_worktree()
    {
        Secret(".env");
        Secret(Path.Combine("src", "Api", "appsettings.Local.json"));

        var worktree = Worktree("feature_x");

        var copied = SecretsMirror.CopyInto(
            SecretsMirror.Root(Project, "backend", "develop"), worktree);

        Assert.Equal(2, copied);
        Assert.True(File.Exists(Path.Combine(worktree, ".env")));
        Assert.True(File.Exists(
            Path.Combine(worktree, "src", "Api", "appsettings.Local.json")));
    }

    [Fact]
    public void A_repository_with_no_mirror_copies_nothing_rather_than_failing()
    {
        Assert.Empty(SecretsMirror.Files(SecretsMirror.Root(Project, "iac", "main")));
        Assert.Equal(0, SecretsMirror.CopyInto(
            SecretsMirror.Root(Project, "iac", "main"), Worktree("main")));
    }

    [Fact]
    public void The_plan_finds_every_worktree_of_the_repository()
    {
        Secret(".env");
        Worktree("develop");
        Worktree("feature_x");
        Directory.CreateDirectory(Path.Combine(Container, "not-a-worktree"));

        var plan = new SecretsHandler().Plan(Project, "backend", Container, "develop");

        Assert.Single(plan.Files);
        Assert.Equal(2, plan.Worktrees.Count);
        Assert.DoesNotContain(plan.Worktrees, w => w.EndsWith("not-a-worktree", StringComparison.Ordinal));
    }

    [Fact]
    public void Distributing_seeds_every_worktree()
    {
        Secret(".env");

        var develop = Worktree("develop");
        var feature = Worktree("feature_x");

        var handler = new SecretsHandler();

        var seeded = handler.Distribute(handler.Plan(Project, "backend", Container, "develop"));

        Assert.Equal(2, seeded);
        Assert.True(File.Exists(Path.Combine(develop, ".env")));
        Assert.True(File.Exists(Path.Combine(feature, ".env")));
    }

    [Fact]
    public void The_manage_menu_offers_secrets_with_its_own_key()
    {
        Assert.Equal("secrets", RepositoryChores.Entries[RepositoryChores.Secrets].Label);

        Assert.Equal(
            ["b", "p", "r", "s", "e"],
            PickerKeys.For(RepositoryChores.Entries));
    }
}
