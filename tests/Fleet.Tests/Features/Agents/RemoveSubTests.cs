using Fleet.Features.Agents.RemoveAgent;
using Fleet.Features.Agents.RemoveAgent.Models;
using Fleet.Features.Agents.StopAgent;
using Fleet.Features.Repositories.AddRepository;
using Fleet.Features.Repositories.AddRepository.Models;
using Fleet.Platform.Git;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;

namespace Fleet.Tests.Features.Agents;

public sealed class RemoveSubTests : IDisposable
{
    private const string Project = "techweb";

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    private readonly FakeMuxDriver _mux = new();

    private readonly MemoryStore _store = new();

    private string? _container;

    public RemoveSubTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                var attributes = File.GetAttributes(file);

                if (attributes.HasFlag(FileAttributes.ReadOnly))
                {
                    File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
                }
            }

            Directory.Delete(_root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private RemoveSubHandler Handler() =>
        new(new RemoveAgentHandler(new GitRunner(), _mux, _store), _store);

    private static RemoveSubCommand Command(
        string slug = "upgrade", string caller = "", bool deleteFolder = false, bool removeAgents = false) =>
        new(Project, slug, caller, deleteFolder, removeAgents);

    private AgentRecord Sub(string slug = "upgrade", string status = OrchestrationStatus.Done)
    {
        var folder = Path.Combine(_root, ".fleet", "orchestrations", slug);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "TASK.md"), "do the thing");

        var sub = new AgentRecord(
            folder, string.Empty, slug, AgentHarness.Orchestrator, "origin/main", false,
            Hidden: true, Open: true, Status: status);

        _store.Save(Project, sub);

        return sub;
    }

    private async Task<AgentRecord> ChildAsync(string branch, string owner = "upgrade")
    {
        if (_container is null)
        {
            var created = await new AddRepositoryHandler(new GitRunner())
                .HandleAsync(AddRepositoryCommand.CreateNew(_root, "backend", "main"));

            Assert.True(created.Succeeded, created.Error);

            _container = created.Value!.Path;
        }

        var slug = branch.Replace('/', '_');

        await new GitRunner().RunAsync(_container, ["worktree", "add", "-b", branch, slug, "main"]);

        var child = new AgentRecord(
            Path.Combine(_container, slug), "backend", branch, AgentHarness.Nvim, "main", true,
            Owner: owner, Claude: null);

        _store.Save(Project, child);

        return child;
    }

    private static async Task CommitAsync(AgentRecord agent)
    {
        await File.WriteAllTextAsync(Path.Combine(agent.Worktree, "feature.txt"), "done");

        var git = new GitRunner();
        await git.RunAsync(agent.Worktree, ["add", "."]);
        await git.RunAsync(
            agent.Worktree,
            ["-c", "user.name=t", "-c", "user.email=t@t", "commit", "-m", "work"]);
    }

    private AgentRecord? Stored(AgentRecord agent) =>
        _store.List(Project).FirstOrDefault(a => PathKey.Same(a.Worktree, agent.Worktree));

    [Fact]
    public async Task By_default_fleet_forgets_the_sub_and_keeps_its_folder()
    {
        var sub = Sub();

        var result = await Handler().HandleAsync(Command());

        Assert.True(result.Succeeded, result.Error);
        Assert.Null(Stored(sub));
        Assert.True(Directory.Exists(sub.Worktree));
        Assert.Contains("folder stays", result.Value!.Note);
    }

    [Fact]
    public async Task Delete_folder_also_discards_the_orchestration_folder()
    {
        var sub = Sub();

        var result = await Handler().HandleAsync(Command(deleteFolder: true));

        Assert.True(result.Succeeded, result.Error);
        Assert.Null(Stored(sub));
        Assert.False(Directory.Exists(sub.Worktree));
    }

    [Fact]
    public async Task Its_agents_are_left_registered_and_running_by_default()
    {
        Sub();
        var child = await ChildAsync("feature/login");
        await _mux.SpawnAsync(new SpawnOptions { Cwd = child.Worktree });

        var result = await Handler().HandleAsync(Command(deleteFolder: true));

        Assert.True(result.Succeeded, result.Error);
        Assert.NotNull(Stored(child));
        Assert.True(Directory.Exists(child.Worktree));
        Assert.Single(await _mux.ListPanesAsync());
        Assert.Equal(["backend/feature/login"], result.Value!.Released);
    }

    [Fact]
    public async Task Agents_left_behind_become_top_level_and_keep_running_claude()
    {
        Sub();
        var child = await ChildAsync("feature/login");

        await Handler().HandleAsync(Command());

        var stored = Stored(child)!;

        Assert.Equal(string.Empty, stored.Owner);
        Assert.True(stored.RunsClaude);
    }

    [Fact]
    public async Task Another_subs_agents_are_never_touched()
    {
        Sub();
        Sub("other");
        var theirs = await ChildAsync("feature/theirs", owner: "other");

        await Handler().HandleAsync(Command(removeAgents: true));

        Assert.Equal("other", Stored(theirs)!.Owner);
        Assert.True(Directory.Exists(theirs.Worktree));
    }

    [Fact]
    public async Task Remove_agents_removes_a_clean_agent_with_its_worktree()
    {
        Sub();
        var child = await ChildAsync("feature/login");

        var result = await Handler().HandleAsync(Command(removeAgents: true));

        Assert.True(result.Succeeded, result.Error);
        Assert.Null(Stored(child));
        Assert.False(Directory.Exists(child.Worktree));
        Assert.Equal(["backend/feature/login"], result.Value!.Removed);
    }

    [Fact]
    public async Task Remove_agents_keeps_an_agent_with_uncommitted_changes_and_names_it()
    {
        Sub();
        var dirty = await ChildAsync("feature/dirty");
        await File.WriteAllTextAsync(Path.Combine(dirty.Worktree, "notes.txt"), "wip");

        var result = await Handler().HandleAsync(Command(removeAgents: true));

        Assert.True(result.Succeeded, result.Error);
        Assert.True(Directory.Exists(dirty.Worktree));
        Assert.Equal(string.Empty, Stored(dirty)!.Owner);
        Assert.Contains("backend/feature/dirty (1 uncommitted change(s))", result.Value!.Kept);
        Assert.Contains("backend/feature/dirty", result.Value.Note);
    }

    [Fact]
    public async Task Remove_agents_keeps_an_agent_with_unpushed_commits()
    {
        Sub();
        var ahead = await ChildAsync("feature/ahead");
        await CommitAsync(ahead);

        var result = await Handler().HandleAsync(Command(removeAgents: true));

        Assert.True(result.Succeeded, result.Error);
        Assert.True(Directory.Exists(ahead.Worktree));
        Assert.NotNull(Stored(ahead));
        Assert.Contains("backend/feature/ahead (1 unpushed commit(s))", result.Value!.Kept);
    }

    [Fact]
    public async Task A_sub_still_working_is_refused_and_nothing_changes()
    {
        var sub = Sub(status: OrchestrationStatus.Working);
        var child = await ChildAsync("feature/login");

        var result = await Handler().HandleAsync(Command(deleteFolder: true, removeAgents: true));

        Assert.False(result.Succeeded);
        Assert.Contains("still working", result.Error);
        Assert.NotNull(Stored(sub));
        Assert.True(Directory.Exists(sub.Worktree));
        Assert.Equal("upgrade", Stored(child)!.Owner);
    }

    [Fact]
    public async Task A_sub_that_never_reported_counts_as_working()
    {
        Sub(status: string.Empty);

        var result = await Handler().HandleAsync(Command());

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task A_sub_cannot_remove_itself()
    {
        var sub = Sub();

        var result = await Handler().HandleAsync(Command(caller: "Upgrade"));

        Assert.False(result.Succeeded);
        Assert.Contains("can't remove itself", result.Error);
        Assert.NotNull(Stored(sub));
    }

    [Fact]
    public async Task A_sub_may_remove_a_finished_sibling()
    {
        Sub();
        Sub("other");

        var result = await Handler().HandleAsync(Command(caller: "other"));

        Assert.True(result.Succeeded, result.Error);
    }

    [Fact]
    public async Task An_unknown_slug_is_named_in_the_error()
    {
        var result = await Handler().HandleAsync(Command(slug: "nope"));

        Assert.False(result.Succeeded);
        Assert.Contains("'nope'", result.Error);
    }

    [Fact]
    public async Task A_repo_agent_is_not_a_sub_even_when_its_branch_matches_the_slug()
    {
        await ChildAsync("upgrade", owner: string.Empty);

        Assert.Null(RemoveSubHandler.Find(_store.List(Project), "upgrade"));
    }

    [Fact]
    public async Task Stopping_a_sub_closes_its_pane_and_keeps_its_record_and_folder()
    {
        var sub = Sub();
        await _mux.SpawnAsync(new SpawnOptions { Cwd = sub.Worktree });

        var result = await new StopAgentHandler(_mux, _store).HandleAsync(Project, sub);

        Assert.True(result.Succeeded, result.Error);
        Assert.Empty(await _mux.ListPanesAsync());
        Assert.True(Directory.Exists(sub.Worktree));
        Assert.False(Stored(sub)!.Open);
    }

    private sealed class MemoryStore : IAgentStore
    {
        private readonly List<AgentRecord> _agents = [];

        public void Save(string project, AgentRecord agent)
        {
            _agents.RemoveAll(a => PathKey.Same(a.Worktree, agent.Worktree));
            _agents.Add(agent);
        }

        public IReadOnlyList<AgentRecord> List(string project) => [.. _agents];

        public void Remove(string project, string worktree) =>
            _agents.RemoveAll(a => PathKey.Same(a.Worktree, worktree));
    }
}
