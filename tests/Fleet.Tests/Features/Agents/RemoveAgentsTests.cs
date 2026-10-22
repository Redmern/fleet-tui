using Fleet.Features.Agents.RemoveAgent;
using Fleet.Platform.Git;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;

namespace Fleet.Tests.Features.Agents;

public sealed class RemoveAgentsTests : IDisposable
{
    private const string Project = "techweb";

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    private readonly MemoryStore _store = new();

    public RemoveAgentsTests() => Directory.CreateDirectory(_root);

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

    private readonly FakeMuxDriver _mux = new();

    private RemoveAgentsHandler Handler() =>
        new(new RemoveAgentHandler(new GitRunner(), _mux, _store, folderRetries: []));

    private AgentRecord Sub(string slug = "upgrade")
    {
        var folder = Path.Combine(_root, ".fleet", "orchestrations", slug);
        Directory.CreateDirectory(folder);

        var sub = new AgentRecord(
            folder, string.Empty, slug, AgentHarness.Orchestrator, "origin/main", false,
            Hidden: true, Open: true, Status: OrchestrationStatus.Done);

        _store.Save(Project, sub);

        return sub;
    }

    [Fact]
    public async Task The_background_note_names_processes_that_could_not_be_terminated()
    {
        var sub = Sub();
        var pane = await _mux.SpawnAsync(new Fleet.Ports.Mux.Models.SpawnOptions { Cwd = sub.Worktree });
        _mux.FailKill(pane, "pane 4242: TerminateJobObject failed with Win32 error 5 (Access is denied)");

        var note = await Handler().HandleAsync(Project, [(sub, true)]);

        Assert.StartsWith("removed upgrade.", note);
        Assert.Contains("could not terminate", note);
        Assert.Contains("Win32 error 5", note);
    }

    [Fact]
    public async Task A_sub_whose_folder_cannot_be_deleted_is_not_saved_back()
    {
        if (!PinnedFolder.Possible)
        {
            return;
        }

        var sub = Sub();
        _store.Saves = 0;

        string note;

        using (PinnedFolder.For(sub.Worktree))
        {
            note = await Handler().HandleAsync(Project, [(sub, true)]);
        }

        Assert.Empty(_store.List(Project));
        Assert.Equal(0, _store.Saves);
        Assert.Contains(sub.Worktree, note);
        Assert.Contains("manually", note);
    }

    [Fact]
    public async Task Removed_subs_are_summed_up_in_one_note()
    {
        var first = Sub("first");
        var second = Sub("second");

        var note = await Handler().HandleAsync(Project, [(first, true), (second, false)]);

        Assert.Empty(_store.List(Project));
        Assert.Equal("removed 2 agent(s).", note);
    }

    [Fact]
    public async Task A_sub_the_store_cannot_remove_is_reported_as_not_removed()
    {
        var sub = Sub();
        _store.RefuseRemove = true;

        var note = await Handler().HandleAsync(Project, [(sub, true)]);

        Assert.Contains("could not remove", note);
        Assert.DoesNotContain("removed upgrade.", note);
        Assert.True(Directory.Exists(sub.Worktree));
    }

    private sealed class MemoryStore : IAgentStore
    {
        private readonly List<AgentRecord> _agents = [];

        public int Saves { get; set; }

        public bool RefuseRemove { get; set; }

        public void Save(string project, AgentRecord agent)
        {
            Saves++;
            _agents.RemoveAll(a => PathKey.Same(a.Worktree, agent.Worktree));
            _agents.Add(agent);
        }

        public bool Replace(string project, AgentRecord agent)
        {
            if (!_agents.Any(a => PathKey.Same(a.Worktree, agent.Worktree)))
            {
                return false;
            }

            Save(project, agent);
            return true;
        }

        public IReadOnlyList<AgentRecord> List(string project) => [.. _agents];

        public bool Remove(string project, string worktree)
        {
            if (RefuseRemove)
            {
                return false;
            }

            _agents.RemoveAll(a => PathKey.Same(a.Worktree, worktree));
            return true;
        }
    }
}
