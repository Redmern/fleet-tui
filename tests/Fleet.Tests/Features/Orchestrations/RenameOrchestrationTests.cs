using Fleet.Features.Orchestrations.RenameOrchestration;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Agents;
using Fleet.Ports.Mux.Enums;
using Fleet.Ports.Mux.Models;
using Fleet.Ports.Agents.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;
using Fleet.Shared.Orchestrations;

namespace Fleet.Tests.Features.Orchestrations;

public sealed class RenameOrchestrationTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    private readonly MemoryStore _store = new();

    public RenameOrchestrationTests() => Directory.CreateDirectory(_root);

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

    private AgentRecord Sub(string slug)
    {
        var folder = OrchestrationPaths.For(_root, slug);
        Directory.CreateDirectory(folder);

        return new AgentRecord(
            folder, string.Empty, slug, AgentHarness.Orchestrator, string.Empty,
            RepositoryWasBare: false, Hidden: true, Open: true, Owner: string.Empty,
            Status: OrchestrationStatus.Working);
    }

    [Fact]
    public void Renaming_moves_the_folder_updates_the_record_and_repoints_children()
    {
        var sub = Sub("old-slug");
        _store.Save("proj", sub);

        var child = new AgentRecord(
            Path.Combine(_root, "backend", "feature_x"), "backend", "feature/x", "nvim",
            "origin/main", RepositoryWasBare: true, Owner: "old-slug");
        _store.Save("proj", child);

        var result = new RenameOrchestrationHandler(_store).Handle("proj", sub, "new-slug");

        Assert.True(result.Succeeded, result.Error);

        var savedSub = Assert.Single(_store.List("proj"), a => AgentHarness.IsOrchestrator(a.Harness));
        Assert.Equal("new-slug", savedSub.Branch);
        Assert.Equal(OrchestrationPaths.For(_root, "old-slug"), savedSub.Worktree);
        Assert.True(Directory.Exists(savedSub.Worktree));

        var savedChild = Assert.Single(_store.List("proj"), a => !AgentHarness.IsOrchestrator(a.Harness));
        Assert.Equal("new-slug", savedChild.Owner);
    }

    [Fact]
    public async Task Renaming_retitles_the_subs_open_tab_so_its_panes_stay_recognisable()
    {
        var mux = new FakeMuxDriver();
        var sub = Sub("old-slug");
        _store.Save("proj", sub);

        var claude = await mux.SpawnAsync(new SpawnOptions { Cwd = sub.Worktree, NewWindow = true });
        await mux.SetTitleAsync(claude, "old-slug");
        var browser = await mux.SplitAsync(new SplitOptions(claude, SplitDirection.Right));
        var other = await mux.SpawnAsync(new SpawnOptions { Cwd = "C:/elsewhere", NewWindow = true });
        await mux.SetTitleAsync(other, "dash");

        var result = await new RenameOrchestrationHandler(_store, mux).HandleAsync("proj", sub, "new-slug");

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("new-slug", mux.TitleOf(claude));
        Assert.Equal("new-slug", mux.TitleOf(browser));
        Assert.Equal("dash", mux.TitleOf(other));

        var renamed = result.Value!;
        Assert.Contains(await mux.ListPanesAsync(), p => AgentPaneMatch.Owns(p, renamed) && p.Id == claude);
    }

    [Fact]
    public void Renaming_onto_an_existing_slug_is_refused()
    {
        var sub = Sub("old-slug");
        _store.Save("proj", sub);
        _store.Save("proj", Sub("taken"));

        var result = new RenameOrchestrationHandler(_store).Handle("proj", sub, "taken");

        Assert.False(result.Succeeded);
        Assert.Equal("old-slug", Assert.Single(
            _store.List("proj"), a => PathKey.Same(a.Worktree, sub.Worktree)).Branch);
    }

    private sealed class MemoryStore : IAgentStore
    {
        private readonly List<AgentRecord> _agents = [];

        public void Save(string project, AgentRecord agent)
        {
            _agents.RemoveAll(a => PathKey.Same(a.Worktree, agent.Worktree));
            _agents.Add(agent);
        }

        public IReadOnlyList<AgentRecord> List(string project) => _agents.ToList();

        public void Remove(string project, string worktree) =>
            _agents.RemoveAll(a => PathKey.Same(a.Worktree, worktree));
    }
}
