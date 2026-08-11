using Fleet.Features.Agents.RemoveAgent;
using Fleet.Features.Agents.StopAgent;
using Fleet.Features.Repositories.AddRepository;
using Fleet.Features.Repositories.AddRepository.Models;
using Fleet.Platform.Git;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux.Models;
using Fleet.Shared.Constants;

namespace Fleet.Tests.Features.Agents;

public sealed class RemoveAgentTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    private readonly FakeMuxDriver _mux = new();

    private readonly RecordingStore _store = new();

    public RemoveAgentTests() => Directory.CreateDirectory(_root);

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

    private RemoveAgentHandler Handler() => new(new GitRunner(), _mux, _store);

    private async Task<AgentRecord> AgentAsync(string branch = "feature/login")
    {
        var created = await new AddRepositoryHandler(new GitRunner())
            .HandleAsync(AddRepositoryCommand.CreateNew(_root, "backend", "main"));

        Assert.True(created.Succeeded, created.Error);

        var container = created.Value!.Path;
        var slug = branch.Replace('/', '_');

        await new GitRunner().RunAsync(container, ["worktree", "add", "-b", branch, slug, "main"]);

        return new AgentRecord(
            Path.Combine(container, slug), "backend", branch, AgentHarness.Claude, "main", true);
    }

    [Fact]
    public async Task Removing_an_agent_takes_its_worktree_with_it()
    {
        var agent = await AgentAsync();

        var result = await Handler().HandleAsync("techweb", agent, deleteWorktree: true);

        Assert.True(result.Succeeded, result.Error);
        Assert.False(Directory.Exists(agent.Worktree));
        Assert.Equal(agent.Worktree, Assert.Single(_store.Removed));
    }

    [Fact]
    public async Task The_repository_and_its_other_worktrees_survive()
    {
        var agent = await AgentAsync();
        var container = Path.Combine(_root, "backend");

        await Handler().HandleAsync("techweb", agent, deleteWorktree: true);

        Assert.True(Directory.Exists(Path.Combine(container, ".git")));
        Assert.True(Directory.Exists(Path.Combine(container, "main")));
    }

    [Fact]
    public async Task The_branch_is_kept_because_removing_an_agent_is_not_deleting_work()
    {
        var agent = await AgentAsync();
        var container = Path.Combine(_root, "backend");

        await Handler().HandleAsync("techweb", agent, deleteWorktree: true);

        var branches = await new GitRunner()
            .RunAsync(container, ["for-each-ref", "--format=%(refname:short)", "refs/heads"]);

        Assert.Contains("feature/login", branches.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_agents_pane_is_killed_before_the_worktree_goes()
    {
        var agent = await AgentAsync();
        await _mux.SpawnAsync(new SpawnOptions { Cwd = agent.Worktree });

        await Handler().HandleAsync("techweb", agent, deleteWorktree: true);

        Assert.Empty(await _mux.ListPanesAsync());
    }

    [Fact]
    public async Task A_clean_worktree_reports_nothing_to_lose()
    {
        var agent = await AgentAsync();

        var state = await Handler().InspectAsync(agent);

        Assert.True(state.Exists);
        Assert.False(state.IsDirty);
    }

    [Fact]
    public async Task An_uncommitted_change_is_reported_before_anything_is_removed()
    {
        var agent = await AgentAsync();
        await File.WriteAllTextAsync(Path.Combine(agent.Worktree, "notes.txt"), "wip");

        var state = await Handler().InspectAsync(agent);

        Assert.True(state.IsDirty);
        Assert.Contains("notes.txt", state.Changed);
    }

    [Fact]
    public async Task Fleets_own_files_never_count_as_uncommitted_work()
    {
        var agent = await AgentAsync();
        Directory.CreateDirectory(Path.Combine(agent.Worktree, ".fleet"));
        await File.WriteAllTextAsync(Path.Combine(agent.Worktree, ".fleet", "ready"), "1");

        var state = await Handler().InspectAsync(agent);

        Assert.False(state.IsDirty);
    }

    [Fact]
    public async Task An_agent_whose_worktree_is_already_gone_still_leaves_the_list()
    {
        var agent = await AgentAsync();
        var container = Path.Combine(_root, "backend");

        await new GitRunner().RunAsync(container, ["worktree", "remove", "--force", agent.Worktree]);

        var state = await Handler().InspectAsync(agent);
        var result = await Handler().HandleAsync("techweb", agent, deleteWorktree: true);

        Assert.False(state.Exists);
        Assert.True(result.Succeeded, result.Error);
        Assert.Single(_store.Removed);
    }

    [Fact]
    public async Task Stopping_an_agent_kills_the_pane_and_keeps_the_worktree()
    {
        var agent = await AgentAsync();
        await _mux.SpawnAsync(new SpawnOptions { Cwd = agent.Worktree });

        var result = await new StopAgentHandler(_mux, _store)
            .HandleAsync("techweb", agent with { Open = true });

        Assert.True(result.Succeeded, result.Error);
        Assert.Empty(await _mux.ListPanesAsync());
        Assert.True(Directory.Exists(agent.Worktree));
        Assert.Empty(_store.Removed);
    }

    [Fact]
    public async Task Stopping_records_that_the_agent_is_no_longer_open()
    {
        var agent = await AgentAsync();
        await _mux.SpawnAsync(new SpawnOptions { Cwd = agent.Worktree });

        await new StopAgentHandler(_mux, _store)
            .HandleAsync("techweb", agent with { Open = true });

        Assert.False(Assert.Single(_store.Saved).Open);
    }

    [Fact]
    public async Task Stopping_an_agent_that_is_not_running_says_so()
    {
        var agent = await AgentAsync();

        var result = await new StopAgentHandler(_mux, _store).HandleAsync("techweb", agent);

        Assert.False(result.Succeeded);
        Assert.Contains("not running", result.Error);
    }

    private sealed class RecordingStore : IAgentStore
    {
        public List<string> Removed { get; } = [];

        public List<AgentRecord> Saved { get; } = [];

        public void Save(string project, AgentRecord agent) => Saved.Add(agent);

        public IReadOnlyList<AgentRecord> List(string project) => [];

        public void Remove(string project, string worktree) => Removed.Add(worktree);
    }

    [Fact]
    public async Task A_worktree_git_has_already_unregistered_is_still_deleted()
    {
        var agent = await AgentAsync();
        var container = Path.Combine(_root, "backend");

        // reproduce git having removed its bookkeeping but not the files
        await new GitRunner().RunAsync(container, ["worktree", "remove", "--force", agent.Worktree]);
        Directory.CreateDirectory(agent.Worktree);
        await File.WriteAllTextAsync(Path.Combine(agent.Worktree, "left-behind.txt"), "x");

        var result = await Handler().HandleAsync("techweb", agent, deleteWorktree: true);

        Assert.True(result.Succeeded, result.Error);
        Assert.False(Directory.Exists(agent.Worktree));
    }
}
