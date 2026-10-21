using Fleet.Features.Agents.ChangeHarness;
using Fleet.Features.Agents.HideAgent;
using Fleet.Features.Agents.OpenAgent;
using Fleet.Features.Agents.StopAgent;
using Fleet.Features.Orchestrations.RenameOrchestration;
using Fleet.Features.Orchestrations.ReportStatus;
using Fleet.Features.Projects.QuitProject;
using Fleet.Features.Projects.RestoreSession;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;

namespace Fleet.Tests.Features.Agents;

// Each handler here works from a record it read earlier; by the time it writes, the record has
// been removed. None of them may bring it back.
public sealed class StaleRecordTests : IDisposable
{
    private const string Project = "techweb";

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    private readonly FakeMuxDriver _mux = new();

    public StaleRecordTests() => Directory.CreateDirectory(_root);

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

    private AgentRecord Sub(bool open = true)
    {
        var folder = Path.Combine(_root, ".fleet", "orchestrations", "upgrade");
        Directory.CreateDirectory(folder);

        return new AgentRecord(
            folder, string.Empty, "upgrade", AgentHarness.Orchestrator, "origin/main", false,
            Hidden: false, Open: open, Status: OrchestrationStatus.Working);
    }

    [Fact]
    public async Task Stopping_a_removed_sub_does_not_bring_it_back()
    {
        var sub = Sub();
        var store = new RemovedStore(sub);
        await _mux.SpawnAsync(new SpawnOptions { Cwd = sub.Worktree });

        await new StopAgentHandler(_mux, store).HandleAsync(Project, sub);

        Assert.Empty(store.Live);
    }

    [Fact]
    public async Task Hiding_a_removed_sub_does_not_bring_it_back()
    {
        var sub = Sub();
        var store = new RemovedStore(sub);

        await new HideAgentHandler(_mux, store).HandleAsync(Project, sub, dashboardWindow: null);

        Assert.Empty(store.Live);
    }

    [Fact]
    public async Task Opening_a_removed_sub_does_not_bring_it_back()
    {
        var sub = Sub(open: false);
        var store = new RemovedStore(sub);

        await new OpenAgentHandler(_mux, store).HandleAsync(Project, sub, _root);

        Assert.Empty(store.Live);
    }

    [Fact]
    public void Changing_what_a_removed_agent_opens_does_not_bring_it_back()
    {
        var sub = Sub();
        var store = new RemovedStore(sub);

        new ChangeHarnessHandler(store).Handle(Project, sub, AgentHarness.Nvim);

        Assert.Empty(store.Live);
    }

    [Fact]
    public void Renaming_a_removed_sub_does_not_bring_it_back()
    {
        var sub = Sub();
        var store = new RemovedStore(sub);

        new RenameOrchestrationHandler(store).Handle(Project, sub, "renamed");

        Assert.Empty(store.Live);
    }

    [Fact]
    public void A_report_from_a_removed_sub_does_not_bring_it_back()
    {
        var sub = Sub();
        var store = new RemovedStore(sub);

        var result = new ReportStatusHandler(store).Handle(Project, "upgrade", "done", "finished");

        Assert.False(result.Succeeded);
        Assert.Empty(store.Live);
    }

    [Fact]
    public async Task Quitting_does_not_bring_back_a_sub_removed_meanwhile()
    {
        var sub = Sub(open: false);
        var store = new RemovedStore(sub);
        await _mux.SpawnAsync(new SpawnOptions { Cwd = _root });
        await _mux.SpawnAsync(new SpawnOptions { Cwd = sub.Worktree });

        await new QuitProjectHandler(_mux, store).HandleAsync(Project, _root, [sub]);

        Assert.Empty(store.Live);
    }

    [Fact]
    public async Task Restoring_does_not_bring_back_a_sub_removed_meanwhile()
    {
        var sub = Sub();
        var store = new RemovedStore(sub);
        await _mux.SpawnAsync(new SpawnOptions { Cwd = _root });

        await new RestoreSessionHandler(_mux, store: store).HandleAsync(Project, _root, [sub]);

        Assert.Empty(store.Live);
    }

    // List still returns what the caller read before the removal; the live records are empty.
    private sealed class RemovedStore(params AgentRecord[] stale) : IAgentStore
    {
        public List<AgentRecord> Live { get; } = [];

        public void Save(string project, AgentRecord agent)
        {
            Live.RemoveAll(a => PathKey.Same(a.Worktree, agent.Worktree));
            Live.Add(agent);
        }

        public bool Replace(string project, AgentRecord agent)
        {
            if (!Live.Any(a => PathKey.Same(a.Worktree, agent.Worktree)))
            {
                return false;
            }

            Save(project, agent);
            return true;
        }

        public IReadOnlyList<AgentRecord> List(string project) => stale;

        public bool Remove(string project, string worktree)
        {
            Live.RemoveAll(a => PathKey.Same(a.Worktree, worktree));
            return true;
        }
    }
}
