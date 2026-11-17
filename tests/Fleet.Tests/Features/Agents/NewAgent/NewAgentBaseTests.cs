using Fleet.Features.Agents.NewAgent;
using Fleet.Features.Agents.NewAgent.Models;
using Fleet.Features.Repositories.AddRepository;
using Fleet.Features.Repositories.AddRepository.Models;
using Fleet.Platform.Git;
using Fleet.Platform.Mux.Fake;
using Fleet.Tests.EndToEnd.MixedTeam;

namespace Fleet.Tests.Features.Agents.NewAgent;

// AC-5, AC-6, AC-7 against real git: a bare container with an origin (a second bare repository), as fleet lays
// repositories out.
public sealed class NewAgentBaseTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    private readonly GitRunner _git = new();

    private readonly MixedTeamRig.MemoryStore _store = new();

    private readonly FakeMuxDriver _mux = new();

    public NewAgentBaseTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
            }

            Directory.Delete(_root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private async Task<string> GitAsync(string directory, params string[] args)
    {
        var result = await _git.RunAsync(directory, ["-c", "user.name=t", "-c", "user.email=t@t", .. args]);

        Assert.True(result.Ok, $"git {string.Join(' ', args)}: {result.Message}");

        return result.Out.Trim();
    }

    private async Task<string> ContainerAsync()
    {
        var created = await new AddRepositoryHandler(_git)
            .HandleAsync(AddRepositoryCommand.CreateNew(_root, "backend", "main"));

        Assert.True(created.Succeeded, created.Error);

        var container = created.Value!.Path;
        var upstream = Path.Combine(_root, "upstream.git");

        await GitAsync(_root, "init", "--bare", "-b", "main", upstream);
        await GitAsync(container, "remote", "add", "origin", upstream);
        await GitAsync(container, "config", "remote.origin.fetch", "+refs/heads/*:refs/remotes/origin/*");
        await GitAsync(container, "push", "origin", "main");

        return container;
    }

    // A new commit on top of a ref, without a worktree. Returns its SHA.
    private async Task<string> CommitOnAsync(string container, string parent, string message) =>
        await GitAsync(container, "commit-tree", $"{parent}^{{tree}}", "-p", parent, "-m", message);

    private async Task<string> LocalBranchAsync(string container, string branch, string parent = "main")
    {
        var sha = await CommitOnAsync(container, parent, $"work on {branch}");

        await GitAsync(container, "branch", "--no-track", branch, sha);

        return sha;
    }

    private async Task<string> OriginBranchAsync(string container, string branch, string parent = "main")
    {
        var sha = await CommitOnAsync(container, parent, $"origin work on {branch}");

        await GitAsync(container, "push", "origin", $"{sha}:refs/heads/{branch}");
        await GitAsync(container, "fetch", "origin");

        return sha;
    }

    private Task<string> TipAsync(string container, string reference) =>
        GitAsync(container, "rev-parse", reference);

    private async Task<bool> ExistsAsync(string container, string reference) =>
        (await _git.RunAsync(container, ["rev-parse", "--verify", "--quiet", reference])).Ok;

    private NewAgentHandler Handler() => new(_git, _mux, _store);

    private static NewAgentCommand Default(string container, string branch) =>
        new("techweb", "backend", container, branch, "main", "claude");

    private static NewAgentCommand WithBase(string container, string branch, string from) =>
        new("techweb", "backend", container, branch, from, "claude", ExactBase: true);

    [Fact]
    public async Task A_base_that_is_a_local_branch_is_where_the_new_branch_starts()
    {
        var container = await ContainerAsync();
        var parent = await LocalBranchAsync(container, "feat/parent");

        var started = await Handler().StartAsync(WithBase(container, "feat/child", "feat/parent"));

        Assert.True(started.Succeeded, started.Error);
        Assert.Equal(parent, await TipAsync(container, "feat/child"));
        Assert.NotEqual(await TipAsync(container, "main"), await TipAsync(container, "feat/child"));
        Assert.Equal("feat/parent", started.Value.Agent.BaseRef);
        Assert.Equal(string.Empty, started.Value.ExistingTip);
        Assert.Equal(string.Empty, BaseNote.For(started.Value, "feat/parent"));
    }

    [Fact]
    public async Task A_local_base_is_used_as_it_is_even_when_origin_has_a_newer_copy_of_it()
    {
        var container = await ContainerAsync();
        var local = await LocalBranchAsync(container, "feat/parent");
        var newer = await OriginBranchAsync(container, "feat/parent", local);

        var started = await Handler().StartAsync(WithBase(container, "feat/child", "feat/parent"));

        Assert.True(started.Succeeded, started.Error);
        Assert.NotEqual(local, newer);
        Assert.Equal(local, await TipAsync(container, "feat/child"));
        Assert.Equal("feat/parent", started.Value.Agent.BaseRef);
    }

    [Fact]
    public async Task A_base_on_origin_is_where_the_new_branch_starts_without_tracking_it()
    {
        var container = await ContainerAsync();
        var remote = await OriginBranchAsync(container, "feat/parent");

        var started = await Handler().StartAsync(WithBase(container, "feat/child", "origin/feat/parent"));

        Assert.True(started.Succeeded, started.Error);
        Assert.Equal(remote, await TipAsync(container, "feat/child"));
        Assert.Equal("origin/feat/parent", started.Value.Agent.BaseRef);
        Assert.False(await ExistsAsync(container, "feat/child@{upstream}"));
        Assert.False(await ExistsAsync(container, "refs/heads/feat/parent"));
    }

    [Fact]
    public async Task Without_a_base_the_new_branch_starts_at_the_repository_default_as_before()
    {
        var container = await ContainerAsync();
        await LocalBranchAsync(container, "feat/parent");

        var started = await Handler().StartAsync(Default(container, "feat/child"));

        Assert.True(started.Succeeded, started.Error);
        Assert.Equal(await TipAsync(container, "main"), await TipAsync(container, "feat/child"));
        Assert.Equal("origin/main", started.Value.Agent.BaseRef);
        Assert.Equal(string.Empty, started.Value.ExistingTip);
    }

    [Fact]
    public async Task A_base_for_a_branch_that_already_exists_is_ignored_and_the_result_names_the_existing_tip()
    {
        var container = await ContainerAsync();
        await LocalBranchAsync(container, "feat/parent");
        var existing = await LocalBranchAsync(container, "feat/child");

        var started = await Handler().StartAsync(WithBase(container, "feat/child", "feat/parent"));

        Assert.True(started.Succeeded, started.Error);
        Assert.Equal(existing, await TipAsync(container, "feat/child"));
        Assert.NotEqual(string.Empty, started.Value.ExistingTip);
        Assert.StartsWith(started.Value.ExistingTip, existing);

        var note = BaseNote.For(started.Value, "feat/parent");

        Assert.Contains("base 'feat/parent' was ignored", note);
        Assert.Contains($"feat/child already exists at {started.Value.ExistingTip}", note);
    }

    [Fact]
    public async Task A_base_for_a_branch_whose_worktree_is_already_there_is_ignored_and_the_result_names_its_tip()
    {
        var container = await ContainerAsync();
        await LocalBranchAsync(container, "feat/parent");
        Assert.True((await Handler().StartAsync(Default(container, "feat/child"))).Succeeded);

        var again = await Handler().StartAsync(WithBase(container, "feat/child", "feat/parent"));

        Assert.True(again.Succeeded, again.Error);
        Assert.Equal(await TipAsync(container, "main"), await TipAsync(container, "feat/child"));
        Assert.StartsWith(again.Value.ExistingTip, await TipAsync(container, "feat/child"));
        Assert.Contains("was ignored", BaseNote.For(again.Value, "feat/parent"));
    }

    [Fact]
    public async Task An_existing_branch_without_a_base_given_adds_no_note()
    {
        var container = await ContainerAsync();
        await LocalBranchAsync(container, "feat/child");

        var started = await Handler().StartAsync(Default(container, "feat/child"));

        Assert.True(started.Succeeded, started.Error);
        Assert.Equal(string.Empty, BaseNote.For(started.Value, string.Empty));
    }

    [Fact]
    public async Task A_branch_that_is_only_on_origin_becomes_a_local_branch_tracking_it_and_not_a_fresh_cut_of_the_default()
    {
        var container = await ContainerAsync();
        var remote = await OriginBranchAsync(container, "feat/shared");

        var started = await Handler().StartAsync(Default(container, "feat/shared"));

        Assert.True(started.Succeeded, started.Error);
        Assert.Equal(remote, await TipAsync(container, "feat/shared"));
        Assert.NotEqual(await TipAsync(container, "main"), await TipAsync(container, "feat/shared"));
        Assert.Equal(
            "origin/feat/shared", await GitAsync(container, "rev-parse", "--abbrev-ref", "feat/shared@{upstream}"));
        Assert.Equal("origin/feat/shared", started.Value.Tracks);
        Assert.Contains("tracks origin/feat/shared", BaseNote.For(started.Value, string.Empty));
    }

    [Fact]
    public async Task A_base_for_a_branch_that_is_only_on_origin_is_ignored_and_the_result_names_the_origin_tip()
    {
        var container = await ContainerAsync();
        await LocalBranchAsync(container, "feat/parent");
        var remote = await OriginBranchAsync(container, "feat/shared");

        var started = await Handler().StartAsync(WithBase(container, "feat/shared", "feat/parent"));

        Assert.True(started.Succeeded, started.Error);
        Assert.Equal(remote, await TipAsync(container, "feat/shared"));
        Assert.StartsWith(started.Value.ExistingTip, remote);

        var note = BaseNote.For(started.Value, "feat/parent");

        Assert.Contains("base 'feat/parent' was ignored", note);
        Assert.Contains($"at {started.Value.ExistingTip}", note);
        Assert.Contains("tracks origin/feat/shared", note);
    }

    [Theory]
    [InlineData("feat/nowhere")]
    [InlineData("origin/feat/nowhere")]
    public async Task A_base_that_does_not_resolve_fails_naming_it_and_creates_nothing(string missing)
    {
        var container = await ContainerAsync();

        var started = await Handler().StartAsync(WithBase(container, "feat/child", missing));

        Assert.False(started.Succeeded);
        Assert.Contains($"'{missing}'", started.Error);
        Assert.Contains("base", started.Error);
        Assert.False(Directory.Exists(Path.Combine(container, "feat_child")));
        Assert.False(await ExistsAsync(container, "refs/heads/feat/child"));
        Assert.Empty(_store.List("techweb"));
    }

    [Fact]
    public async Task A_base_that_is_only_on_origin_under_its_short_name_fails_and_says_to_name_origin()
    {
        var container = await ContainerAsync();
        await OriginBranchAsync(container, "feat/parent");

        var started = await Handler().StartAsync(WithBase(container, "feat/child", "feat/parent"));

        Assert.False(started.Succeeded);
        Assert.Contains("origin/feat/parent", started.Error);
        Assert.False(await ExistsAsync(container, "refs/heads/feat/child"));
    }
}
