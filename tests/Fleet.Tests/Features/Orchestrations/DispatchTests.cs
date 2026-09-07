using Fleet.Features.Orchestrations.Dispatch;
using Fleet.Features.Orchestrations.Dispatch.Models;
using Fleet.Platform.Harness;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux.Models;
using Fleet.Shared.Constants;
using Fleet.Shared.Orchestrations;

namespace Fleet.Tests.Features.Orchestrations;

public sealed class DispatchTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    private readonly FakeMuxDriver _mux = new();

    private readonly RecordingStore _store = new();

    public DispatchTests() => Directory.CreateDirectory(_root);

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

    private DispatchHandler Handler =>
        new(_mux, _store, new NullHarnessConfig(), TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);

    private DispatchCommand Command(string prompt, string caller = "") =>
        new("techweb", _root, prompt, caller);

    [Fact]
    public async Task It_scaffolds_the_folder_with_the_prompt_kept_verbatim()
    {
        var reply = await Handler.HandleAsync(Command("Add a \"create story\" endpoint\nin backend"), "2026-08-15T00:00:00Z");

        Assert.True(reply.Succeeded, reply.Error);

        var folder = reply.Value!.Folder;

        Assert.True(File.Exists(OrchestrationPaths.InstructionsFile(folder)));
        Assert.True(Directory.Exists(OrchestrationPaths.ReportsFolder(folder)));

        var task = File.ReadAllText(OrchestrationPaths.TaskFile(folder));

        Assert.Contains("Add a \"create story\" endpoint\nin backend", task);
    }

    [Fact]
    public async Task It_records_a_hidden_open_working_orchestrator_named_by_the_slug()
    {
        await Handler.HandleAsync(Command("upgrade the node runtime"), "t");

        var record = Assert.Single(_store.Saved);

        Assert.Equal(AgentHarness.Orchestrator, record.Harness);
        Assert.True(record.Hidden);
        Assert.True(record.Open);
        Assert.Equal(OrchestrationStatus.Working, record.Status);
        Assert.Equal(string.Empty, record.Repository);
        Assert.Equal("upgrade-the-node-runtime", record.Branch);
    }

    [Fact]
    public async Task It_stamps_the_caller_so_the_sub_can_be_grouped()
    {
        await Handler.HandleAsync(Command("do a thing", caller: "parent-slug"), "t");

        Assert.Equal("parent-slug", Assert.Single(_store.Saved).Owner);
    }

    [Fact]
    public async Task The_pane_runs_an_interactive_claude()
    {
        var reply = await Handler.HandleAsync(Command("start work"), "t");

        var panes = await _mux.ListPanesAsync();

        Assert.Single(panes, p =>
            p.Cwd == reply.Value!.Folder
            && _mux.ArgsFor(p.Id).SequenceEqual(new[] { AgentHarness.Claude }));
    }

    [Fact]
    public async Task It_opens_a_file_browser_beside_claude_in_the_same_window()
    {
        var reply = await Handler.HandleAsync(Command("start work"), "t");

        var panes = await _mux.ListPanesAsync();

        var claude = panes.Single(p =>
            p.Cwd == reply.Value!.Folder
            && _mux.ArgsFor(p.Id).SequenceEqual(new[] { AgentHarness.Claude }));
        var browser = panes.Single(p =>
            p.Cwd == reply.Value!.Folder
            && _mux.ArgsFor(p.Id).SequenceEqual(
                AgentHarness.BrowseCommandFor($"{reply.Value.Slug} files")));

        Assert.Equal(claude.WindowId, browser.WindowId);
        Assert.Equal(claude.TabId, browser.TabId);
        Assert.Equal(reply.Value!.Slug, claude.Title);
        Assert.Equal(reply.Value!.Slug, browser.Title);
        Assert.Equal($"{reply.Value!.Slug} files", browser.PaneTitle);
    }

    [Fact]
    public async Task The_claude_pane_forces_session_persistence_so_it_can_be_resumed()
    {
        var reply = await Handler.HandleAsync(Command("start work"), "t");

        var panes = await _mux.ListPanesAsync();
        var claude = panes.Single(p =>
            p.Cwd == reply.Value!.Folder
            && _mux.ArgsFor(p.Id).SequenceEqual(new[] { AgentHarness.Claude }));

        var env = _mux.EnvFor(claude.Id);

        Assert.Equal("1", env["CLAUDE_CODE_FORCE_SESSION_PERSISTENCE"]);
        Assert.Equal(string.Empty, env["CLAUDE_CODE_CHILD_SESSION"]);
    }

    [Fact]
    public async Task It_types_the_kickoff_into_the_claude_pane_so_the_sub_starts_itself()
    {
        var reply = await Handler.HandleAsync(Command("start work"), "t");

        var panes = await _mux.ListPanesAsync();
        var claude = panes.Single(p =>
            p.Cwd == reply.Value!.Folder
            && _mux.ArgsFor(p.Id).SequenceEqual(new[] { AgentHarness.Claude }));

        var sent = _mux.SentTo(claude.Id);

        Assert.Contains(sent, s => s.Contains(AgentHarness.OrchestratorKickoff));
        Assert.Contains("\r", sent);
    }

    [Fact]
    public async Task Two_dispatches_of_the_same_prompt_get_distinct_slugs()
    {
        var first = await Handler.HandleAsync(Command("same task"), "t");
        var second = await Handler.HandleAsync(Command("same task"), "t");

        Assert.Equal("same-task", first.Value!.Slug);
        Assert.Equal("same-task-2", second.Value!.Slug);
    }

    [Fact]
    public async Task A_blank_prompt_fails_before_touching_the_disk()
    {
        var reply = await Handler.HandleAsync(Command("   "), "t");

        Assert.False(reply.Succeeded);
        Assert.Empty(_store.Saved);
        Assert.False(Directory.Exists(OrchestrationPaths.Root(_root)));
    }

    [Fact]
    public async Task The_record_is_saved_before_the_pane_so_a_spawn_failure_still_lists_the_sub()
    {
        await Handler.HandleAsync(Command("work"), "t");

        var record = Assert.Single(_store.Saved);

        Assert.True(record.Open);
        Assert.Equal(OrchestrationStatus.Working, record.Status);
    }

    private sealed class RecordingStore : IAgentStore
    {
        public List<AgentRecord> Saved { get; } = [];

        public void Save(string project, AgentRecord agent) => Saved.Add(agent);

        public IReadOnlyList<AgentRecord> List(string project) => Saved;

        public void Remove(string project, string worktree) =>
            Saved.RemoveAll(a => a.Worktree == worktree);
    }
}
