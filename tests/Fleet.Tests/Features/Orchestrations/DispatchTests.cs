using Fleet.Features.Orchestrations.Dispatch;
using Fleet.Features.Orchestrations.Dispatch.Models;
using Fleet.Platform.Harness;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux.Models;
using Fleet.Ports.Orchestrations;
using Fleet.Ports.Settings;
using Fleet.Shared.Constants;
using Fleet.Shared.Orchestrations;
using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;

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
    public async Task A_project_level_instructions_override_replaces_the_default_how_you_work_section()
    {
        Directory.CreateDirectory(ProjectConfigPaths.Root(_root));
        File.WriteAllText(
            ProjectConfigPaths.InstructionsFile(_root), "Only ever touch the api/ folder.");

        var reply = await Handler.HandleAsync(Command("start work"), "t");

        var instructions = File.ReadAllText(OrchestrationPaths.InstructionsFile(reply.Value!.Folder));

        Assert.Contains("Only ever touch the api/ folder.", instructions);
        Assert.DoesNotContain(OrchestrationText.DefaultHowYouWork, instructions);
    }

    [Fact]
    public async Task Aidlc_mode_off_never_adds_a_process_section_even_with_a_doubled_trigger()
    {
        var handler = new DispatchHandler(
            _mux, _store, new NullHarnessConfig(), TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero,
            settings: new FakeSettingsStore(SettingsConfig.Default.WithAidlcMode(AidlcMode.Off)));

        var reply = await handler.HandleAsync(Command(",,do the thing"), "t");

        var instructions = File.ReadAllText(OrchestrationPaths.InstructionsFile(reply.Value!.Folder));

        Assert.DoesNotContain("## Process", instructions);
    }

    [Fact]
    public async Task Aidlc_mode_on_adds_a_process_section_for_a_plain_prompt()
    {
        var handler = new DispatchHandler(
            _mux, _store, new NullHarnessConfig(), TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero,
            settings: new FakeSettingsStore(SettingsConfig.Default.WithAidlcMode(AidlcMode.On)));

        var reply = await handler.HandleAsync(Command("do the thing"), "t");

        var instructions = File.ReadAllText(OrchestrationPaths.InstructionsFile(reply.Value!.Folder));

        Assert.Contains("## Process", instructions);
        Assert.Contains(OrchestrationText.DefaultAidlc, instructions);
    }

    [Fact]
    public async Task Aidlc_mode_manual_ignores_a_plain_prompt()
    {
        var handler = new DispatchHandler(
            _mux, _store, new NullHarnessConfig(), TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero,
            settings: new FakeSettingsStore(SettingsConfig.Default.WithAidlcMode(AidlcMode.Manual)));

        var reply = await handler.HandleAsync(Command("do the thing"), "t");

        var instructions = File.ReadAllText(OrchestrationPaths.InstructionsFile(reply.Value!.Folder));

        Assert.DoesNotContain("## Process", instructions);
    }

    [Fact]
    public async Task Aidlc_mode_manual_adds_a_process_section_when_the_prompt_still_starts_with_the_trigger()
    {
        var handler = new DispatchHandler(
            _mux, _store, new NullHarnessConfig(), TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero,
            settings: new FakeSettingsStore(SettingsConfig.Default.WithAidlcMode(AidlcMode.Manual)));

        var reply = await handler.HandleAsync(Command(",do the thing"), "t");

        var folder = reply.Value!.Folder;
        var instructions = File.ReadAllText(OrchestrationPaths.InstructionsFile(folder));
        var task = File.ReadAllText(OrchestrationPaths.TaskFile(folder));

        Assert.Contains("## Process", instructions);
        Assert.Contains(OrchestrationText.DefaultAidlc, instructions);
        Assert.Contains("do the thing", task);
        Assert.DoesNotContain(",do the thing", task);
        Assert.Equal("do-the-thing", reply.Value!.Slug);
    }

    [Fact]
    public async Task A_project_level_aidlc_override_replaces_the_default_process_section()
    {
        Directory.CreateDirectory(ProjectConfigPaths.Root(_root));
        File.WriteAllText(ProjectConfigPaths.AidlcFile(_root), "Skip straight to implementing.");

        var handler = new DispatchHandler(
            _mux, _store, new NullHarnessConfig(), TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero,
            settings: new FakeSettingsStore(SettingsConfig.Default.WithAidlcMode(AidlcMode.On)));

        var reply = await handler.HandleAsync(Command("do the thing"), "t");

        var instructions = File.ReadAllText(OrchestrationPaths.InstructionsFile(reply.Value!.Folder));

        Assert.Contains("Skip straight to implementing.", instructions);
        Assert.DoesNotContain(OrchestrationText.DefaultAidlc, instructions);
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
    public async Task A_namer_that_returns_a_name_picks_the_slug_over_the_raw_prompt()
    {
        var namer = new FakeSlugNamer(name: "fix login timeout");

        var handler = new DispatchHandler(
            _mux, _store, new NullHarnessConfig(), TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero,
            namer: namer);

        var reply = await handler.HandleAsync(
            Command("please go make the login page stop timing out so fast, it's annoying"), "t");

        Assert.Equal("fix-login-timeout", reply.Value!.Slug);
    }

    [Fact]
    public async Task A_namer_that_fails_falls_back_to_the_heuristic_slug()
    {
        var namer = new FakeSlugNamer(throws: true);

        var handler = new DispatchHandler(
            _mux, _store, new NullHarnessConfig(), TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero,
            namer: namer);

        var reply = await handler.HandleAsync(Command("upgrade the node runtime"), "t");

        Assert.Equal("upgrade-the-node-runtime", reply.Value!.Slug);
    }

    [Fact]
    public async Task A_namer_that_returns_nothing_falls_back_to_the_heuristic_slug()
    {
        var namer = new FakeSlugNamer(name: null);

        var handler = new DispatchHandler(
            _mux, _store, new NullHarnessConfig(), TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero,
            namer: namer);

        var reply = await handler.HandleAsync(Command("upgrade the node runtime"), "t");

        Assert.Equal("upgrade-the-node-runtime", reply.Value!.Slug);
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

    private sealed class FakeSettingsStore(SettingsConfig config) : ISettingsStore
    {
        public SettingsConfig Load(string project) => config;

        public void Save(string project, SettingsConfig config)
        {
        }
    }

    private sealed class FakeSlugNamer(string? name = null, bool throws = false) : ISlugNamer
    {
        public Task<string?> NameAsync(string prompt, CancellationToken ct = default) =>
            throws ? throw new InvalidOperationException("claude is unreachable") : Task.FromResult(name);
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
