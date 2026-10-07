using Fleet.Features.Orchestrations.Dispatch;
using Fleet.Features.Orchestrations.Dispatch.Models;
using Fleet.Platform.Aidlc;
using Fleet.Platform.Harness;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux.Models;
using Fleet.Ports.Orchestrations;
using Fleet.Ports.Settings;
using Fleet.Shared.Aidlc;
using Fleet.Shared.Aidlc.Enums;
using Fleet.Shared.Constants;
using Fleet.Shared.Orchestrations;
using Fleet.Shared.Results;
using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;

namespace Fleet.Tests.Features.Orchestrations;

public sealed class DispatchTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    private readonly FakeMuxDriver _mux = new();

    private readonly RecordingStore _store = new();

    private readonly JsonIntentStore _intents = new();

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
        new(_mux, _store, new NullHarnessConfig(), TimeSpan.Zero, TimeSpan.Zero);

    private DispatchCommand Command(string prompt, string caller = "") =>
        new("techweb", _root, prompt, caller);

    private static IReadOnlyList<string> Sub(string slug) =>
        AgentHarness.OrchestratorCommand(
            resume: false, launch: ClaudeLaunch.ForAgent("techweb", string.Empty, slug, true, SettingsDefaults.Models));

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

    private DispatchHandler Aidlc(AidlcSettings aidlc) =>
        new(
            _mux, _store, new NullHarnessConfig(), TimeSpan.Zero, TimeSpan.Zero,
            settings: new FakeSettingsStore(SettingsConfig.Default.WithAidlc(aidlc)),
            intents: _intents);

    private DispatchHandler Aidlc(AidlcMode mode) => Aidlc(AidlcSettings.Default with { Mode = mode });

    private static string InstructionsOf(DispatchReply reply) =>
        File.ReadAllText(OrchestrationPaths.InstructionsFile(reply.Folder));

    [Fact]
    public async Task Aidlc_off_adds_no_process_and_no_record_even_with_a_profile_prefix()
    {
        var reply = (await Aidlc(AidlcMode.Off).HandleAsync(Command("feature: do the thing"), "t")).Value!;

        Assert.DoesNotContain("## Process", InstructionsOf(reply));
        Assert.False(File.Exists(OrchestrationPaths.StateFile(reply.Folder)));
        Assert.False(File.Exists(OrchestrationPaths.AuditFile(reply.Folder)));
        Assert.Contains("feature: do the thing", File.ReadAllText(OrchestrationPaths.TaskFile(reply.Folder)));
    }

    [Fact]
    public async Task Aidlc_on_runs_a_plain_prompt_with_the_project_default_profile()
    {
        var reply = (await Aidlc(AidlcMode.On).HandleAsync(Command("do the thing"), "2026-10-02T10:00:00Z")).Value!;

        var instructions = InstructionsOf(reply);
        var state = _intents.Load(reply.Folder)!;

        Assert.Contains("## Process", instructions);
        Assert.Contains("**express** profile", instructions);
        Assert.Equal(Profile.Express, state.Profile);
        Assert.Equal(reply.Slug, state.Slug);
        Assert.Equal("2026-10-02T10:00:00Z", state.Created);
        Assert.Equal(StageState.Done, state.Stages[0].State);
        Assert.Equal(ProfileCatalog.StagesOf(Profile.Express), state.Stages.Select(s => s.Stage));
        Assert.Contains("AIDLC express", reply.Note);
    }

    [Fact]
    public async Task The_record_logs_creation_and_where_the_profile_came_from()
    {
        var reply = (await Aidlc(AidlcMode.On).HandleAsync(Command("do the thing"), "t")).Value!;

        var audit = _intents.Audit(reply.Folder);

        Assert.Equal([AuditEvent.IntentCreated, AuditEvent.ProfileSet], audit.Select(e => e.Event));
        Assert.Equal(reply.Slug, audit[0].Detail);
        Assert.Equal("express (the project default)", audit[1].Detail);
    }

    [Fact]
    public async Task The_project_default_profile_comes_from_the_settings()
    {
        var handler = Aidlc(AidlcSettings.Default with { Mode = AidlcMode.On, DefaultProfile = Profile.Bugfix });

        var reply = (await handler.HandleAsync(Command("the login loops"), "t")).Value!;

        Assert.Equal(Profile.Bugfix, _intents.Load(reply.Folder)!.Profile);
        Assert.Contains("**bugfix** profile", InstructionsOf(reply));
    }

    [Fact]
    public async Task A_profile_prefix_picks_the_profile_and_is_stripped_from_the_task()
    {
        var reply = (await Aidlc(AidlcMode.On).HandleAsync(Command("feature: add oauth login"), "t")).Value!;

        var task = File.ReadAllText(OrchestrationPaths.TaskFile(reply.Folder));

        Assert.Equal(Profile.Feature, _intents.Load(reply.Folder)!.Profile);
        Assert.Contains("add oauth login", task);
        Assert.DoesNotContain("feature:", task);
        Assert.Equal("add-oauth-login", reply.Slug);
        Assert.Equal("feature (from the task prefix)", _intents.Audit(reply.Folder)[1].Detail);
    }

    [Fact]
    public async Task Aidlc_manual_ignores_a_plain_prompt()
    {
        var reply = (await Aidlc(AidlcMode.Manual).HandleAsync(Command("do the thing"), "t")).Value!;

        Assert.DoesNotContain("## Process", InstructionsOf(reply));
        Assert.Null(_intents.Load(reply.Folder));
    }

    [Fact]
    public async Task Aidlc_manual_no_longer_treats_a_doubled_trigger_as_a_request()
    {
        var reply = (await Aidlc(AidlcMode.Manual).HandleAsync(Command(",do the thing"), "t")).Value!;

        Assert.DoesNotContain("## Process", InstructionsOf(reply));
        Assert.Null(_intents.Load(reply.Folder));
    }

    [Fact]
    public async Task Aidlc_manual_applies_when_the_task_starts_with_a_profile()
    {
        var reply = (await Aidlc(AidlcMode.Manual).HandleAsync(Command("research: how do others gate merges"), "t")).Value!;

        Assert.Contains("**research** profile", InstructionsOf(reply));
        Assert.Equal(Profile.Research, _intents.Load(reply.Folder)!.Profile);
    }

    [Fact]
    public async Task A_profile_argument_picks_the_profile_when_there_is_no_prefix()
    {
        var reply = (await Aidlc(AidlcMode.On).HandleAsync(Command("tidy the store") with { Profile = "Refactor" }, "t")).Value!;

        Assert.Equal(Profile.Refactor, _intents.Load(reply.Folder)!.Profile);
        Assert.Equal("refactor (from the dispatch argument)", _intents.Audit(reply.Folder)[1].Detail);
    }

    [Fact]
    public async Task A_prefix_wins_over_the_profile_argument()
    {
        var reply = (await Aidlc(AidlcMode.On).HandleAsync(Command("bugfix: the login loops") with { Profile = "feature" }, "t")).Value!;

        Assert.Equal(Profile.Bugfix, _intents.Load(reply.Folder)!.Profile);
    }

    [Fact]
    public async Task In_manual_mode_a_profile_argument_without_a_prefix_does_not_apply_aidlc()
    {
        var reply = (await Aidlc(AidlcMode.Manual).HandleAsync(Command("tidy the store") with { Profile = "refactor" }, "t")).Value!;

        Assert.DoesNotContain("## Process", InstructionsOf(reply));
        Assert.Null(_intents.Load(reply.Folder));
    }

    [Fact]
    public async Task In_manual_mode_a_prefix_still_wins_over_the_profile_argument()
    {
        var reply = (await Aidlc(AidlcMode.Manual).HandleAsync(Command("bugfix: the login loops") with { Profile = "feature" }, "t")).Value!;

        Assert.Equal(Profile.Bugfix, _intents.Load(reply.Folder)!.Profile);
    }

    [Fact]
    public async Task An_unknown_profile_argument_fails_before_touching_the_disk()
    {
        var reply = await Aidlc(AidlcMode.On).HandleAsync(Command("do it") with { Profile = "epic" }, "t");

        Assert.False(reply.Succeeded);
        Assert.Contains("'epic' is not an AIDLC profile", reply.Error);
        Assert.Contains("express, bugfix, feature, refactor, research", reply.Error);
        Assert.False(Directory.Exists(OrchestrationPaths.Root(_root)));
    }

    [Fact]
    public async Task Parts_switched_off_are_recorded_as_skipped_and_left_out_of_the_process()
    {
        var aidlc = AidlcSettings.Default with { Mode = AidlcMode.On, DefaultProfile = Profile.Feature };
        var handler = Aidlc(aidlc.With(AidlcPart.Review, on: false).With(AidlcPart.SpecGate, on: false));

        var reply = (await handler.HandleAsync(Command("add oauth"), "t")).Value!;

        var state = _intents.Load(reply.Folder)!;
        var review = state.Stages.Single(s => s.Stage == Stage.Review);
        var specify = state.Stages.Single(s => s.Stage == Stage.Specify);

        Assert.Equal(StageState.Skipped, review.State);
        Assert.Equal("off in settings", review.Reason);
        Assert.False(specify.HumanGate);
        Assert.Contains(_intents.Audit(reply.Folder), e => e.Event == AuditEvent.StageSkipped && e.Detail == "review: off in settings");
        Assert.Contains("Skipped: review (off in settings).", InstructionsOf(reply));
    }

    [Fact]
    public async Task A_project_aidlc_file_is_appended_to_the_rendered_process()
    {
        Directory.CreateDirectory(ProjectConfigPaths.Root(_root));
        File.WriteAllText(ProjectConfigPaths.AidlcFile(_root), "Always run the e2e suite too.");

        var reply = (await Aidlc(AidlcMode.On).HandleAsync(Command("do the thing"), "t")).Value!;

        var instructions = InstructionsOf(reply);

        Assert.Contains("**Deliver**", instructions);
        Assert.Contains("### Project guidance", instructions);
        Assert.Contains("Always run the e2e suite too.", instructions);
        Assert.True(
            instructions.IndexOf("**Deliver**", StringComparison.Ordinal)
            < instructions.IndexOf("Always run the e2e suite too.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_aidlc_file_still_holding_the_old_built_in_text_is_ignored()
    {
        Directory.CreateDirectory(ProjectConfigPaths.Root(_root));
        File.WriteAllText(ProjectConfigPaths.AidlcFile(_root), OrchestrationText.ClassicAidlc.ReplaceLineEndings("\r\n") + "\r\n");

        var reply = (await Aidlc(AidlcMode.On).HandleAsync(Command("do the thing"), "t")).Value!;

        var instructions = InstructionsOf(reply);

        Assert.DoesNotContain("### Project guidance", instructions);
        Assert.DoesNotContain("Follow this cycle for every unit of work", instructions);
    }

    [Fact]
    public async Task Without_an_intent_store_dispatch_still_renders_the_process()
    {
        var handler = new DispatchHandler(
            _mux, _store, new NullHarnessConfig(), TimeSpan.Zero, TimeSpan.Zero,
            settings: new FakeSettingsStore(SettingsConfig.Default.WithAidlcMode(AidlcMode.On)));

        var reply = (await handler.HandleAsync(Command("do the thing"), "t")).Value!;

        Assert.Contains("## Process", InstructionsOf(reply));
        Assert.False(File.Exists(OrchestrationPaths.StateFile(reply.Folder)));
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
    public async Task The_pane_runs_claude_alone_inside_nvim()
    {
        var reply = await Handler.HandleAsync(Command("start work"), "t");

        var panes = await _mux.ListPanesAsync();

        Assert.Single(panes, p =>
            p.Cwd == reply.Value!.Folder
            && _mux.ArgsFor(p.Id).SequenceEqual(Sub("start-work")));
    }

    [Fact]
    public async Task It_opens_claude_alone_with_no_file_browser()
    {
        var reply = await Handler.HandleAsync(Command("start work"), "t");

        var panes = await _mux.ListPanesAsync();

        var claude = Assert.Single(panes, p => p.Cwd == reply.Value!.Folder);

        Assert.Equal(Sub("start-work"), _mux.ArgsFor(claude.Id));
        Assert.Equal(reply.Value!.Slug, claude.Title);
    }

    [Fact]
    public async Task The_nvim_host_forces_session_persistence_so_claude_can_be_resumed()
    {
        await Handler.HandleAsync(Command("start work"), "t");

        var boot = AgentHarness.CommandFor(AgentHarness.Orchestrator)[2];

        Assert.Contains("CLAUDE_CODE_FORCE_SESSION_PERSISTENCE='1'", boot);
        Assert.Contains("CLAUDE_CODE_CHILD_SESSION=nil", boot);
    }

    [Fact]
    public async Task It_leaves_the_kickoff_in_the_instruction_file_so_nvim_hands_it_to_claude()
    {
        var reply = await Handler.HandleAsync(Command("start work"), "t");

        var inbox = Path.Combine(reply.Value!.Folder, ".fleet", AgentHarness.AgentInstructionFile);

        Assert.Equal(AgentHarness.OrchestratorKickoff, File.ReadAllText(inbox));
        Assert.All(await _mux.ListPanesAsync(), p => Assert.Empty(_mux.SentTo(p.Id)));
    }

    [Fact]
    public async Task In_nvim_the_kickoff_is_left_before_the_spawn_and_marked_unseen_without_waiting_for_ready()
    {
        var handler = new DispatchHandler(
            _mux, _store, new NullHarnessConfig(), TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(50));

        var started = DateTime.UtcNow;
        var reply = await handler.HandleAsync(Command("start work"), "t");

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10));
        var inbox = Path.Combine(reply.Value!.Folder, ".fleet");
        Assert.Equal("0", File.ReadAllText(Path.Combine(inbox, AgentHarness.InstructionSeenFile)));
        Assert.Equal(AgentHarness.OrchestratorKickoff, File.ReadAllText(Path.Combine(inbox, AgentHarness.AgentInstructionFile)));
    }

    [Fact]
    public void The_nvim_pump_waits_for_claude_to_be_ready_and_for_the_file_to_settle()
    {
        var boot = AgentHarness.OrchestratorCommand(resume: false)[2];

        Assert.Contains($"local readyf='.fleet/{AgentHarness.ClaudeReadyFile}' local fleet_boot=os.time()", boot);
        Assert.Contains($"if age<{AgentHarness.InstructionSettleSeconds} then return end", boot);
        Assert.Contains(
            $"if vim.fn.getftime(readyf)<fleet_boot and age<{AgentHarness.ReadyFallbackSeconds} then return end", boot);
        Assert.True(
            boot.IndexOf("local age=", StringComparison.Ordinal)
            < boot.IndexOf("pcall(vim.fn.writefile,{tostring(m)},seenf)", StringComparison.Ordinal));
    }

    [Fact]
    public void The_claude_only_nvim_drops_the_empty_no_name_buffer()
    {
        var boot = AgentHarness.OrchestratorCommand(resume: false)[2];

        Assert.Contains("pcall(vim.api.nvim_buf_delete, b, {force=true})", boot);
        Assert.Contains("vim.api.nvim_buf_get_name(b)==''", boot);
    }

    [Fact]
    public void Ctrl_hjkl_in_the_claude_only_nvim_moves_between_wezterm_panes()
    {
        var boot = AgentHarness.OrchestratorCommand(resume: false)[2];

        Assert.Contains(
            "{{'<C-h>','h','Left',{'n','t'}},{'<C-j>','j','Down',{'n','t'}},"
            + "{'<C-k>','k','Up',{'n','t'}},{'<C-l>','l','Right',{'n','t'}}}",
            boot);
        Assert.Contains("'cli','activate-pane-direction',d", boot);
        Assert.Contains("{buffer=tb}", boot);
    }

    [Fact]
    public void Ctrl_hjkl_moves_to_an_nvim_window_first_when_one_is_in_that_direction()
    {
        var boot = AgentHarness.OrchestratorCommand(resume: false)[2];

        Assert.Contains("if vim.fn.winnr(k)~=vim.fn.winnr() then vim.cmd('stopinsert') vim.cmd('wincmd '..k)", boot);
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
    public async Task A_dispatched_sub_starts_in_the_projects_hidden_workspace_and_still_gets_its_kickoff()
    {
        var mux = new FakeMuxDriver(workspaces: true);
        var visible = await mux.SpawnAsync(new SpawnOptions { SessionName = "techweb", Cwd = _root });
        await mux.FocusPaneAsync(visible);

        var handler = new DispatchHandler(
            mux, _store, new NullHarnessConfig(), TimeSpan.Zero, TimeSpan.Zero,
            settings: new FakeSettingsStore(SettingsConfig.Default.WithSubOrchestratorsInNvim(false)));

        var reply = await handler.HandleAsync(Command("do the thing"), "t");

        Assert.True(reply.Succeeded, reply.Error);
        Assert.True(Assert.Single(_store.Saved).Hidden);

        var sub = Assert.Single(await mux.ListPanesAsync(), p => p.Id != visible);
        Assert.Equal(FleetWorkspaces.HiddenFor("techweb"), sub.SessionName);
        Assert.False(sub.IsActive);
        Assert.True(Assert.Single(await mux.ListPanesAsync(), p => p.Id == visible).IsActive);
        Assert.DoesNotContain(mux.Calls, c => c == "show" || c.StartsWith("open-window", StringComparison.Ordinal));
        Assert.Equal([AgentHarness.OrchestratorKickoff, "\r"], mux.SentTo(sub.Id));
    }

    [Fact]
    public async Task Without_workspaces_a_dispatched_sub_starts_in_the_shared_hidden_workspace()
    {
        await _mux.SpawnAsync(new SpawnOptions { SessionName = "techweb", Cwd = _root });

        var reply = await Handler.HandleAsync(Command("do the thing"), "t");

        var sub = Assert.Single(await _mux.ListPanesAsync(), p => p.Cwd == reply.Value!.Folder);
        Assert.Equal(FleetWorkspaces.Hidden, sub.SessionName);
    }

    [Fact]
    public async Task A_namer_that_returns_a_name_picks_the_slug_over_the_raw_prompt()
    {
        var namer = new FakeSlugNamer(name: "fix login timeout");

        var handler = new DispatchHandler(
            _mux, _store, new NullHarnessConfig(), TimeSpan.Zero, TimeSpan.Zero,
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
            _mux, _store, new NullHarnessConfig(), TimeSpan.Zero, TimeSpan.Zero,
            namer: namer);

        var reply = await handler.HandleAsync(Command("upgrade the node runtime"), "t");

        Assert.Equal("upgrade-the-node-runtime", reply.Value!.Slug);
    }

    [Fact]
    public async Task A_namer_that_returns_nothing_falls_back_to_the_heuristic_slug()
    {
        var namer = new FakeSlugNamer(name: null);

        var handler = new DispatchHandler(
            _mux, _store, new NullHarnessConfig(), TimeSpan.Zero, TimeSpan.Zero,
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

    [Fact]
    public async Task With_main_off_and_subs_on_the_sub_runs_inside_nvim_and_nothing_is_typed_into_it()
    {
        var handler = new DispatchHandler(
            _mux, _store, new NullHarnessConfig(), TimeSpan.Zero, TimeSpan.Zero,
            settings: new FakeSettingsStore(SettingsConfig.Default
                .WithMainOrchestratorInNvim(false)
                .WithSubOrchestratorsInNvim(true)));

        var reply = await handler.HandleAsync(Command("do the thing"), "t");

        var pane = Assert.Single(await _mux.ListPanesAsync());
        Assert.Equal(Sub("do-the-thing"), _mux.ArgsFor(pane.Id));
        Assert.Empty(_mux.EnvFor(pane.Id));
        Assert.Empty(_mux.SentTo(pane.Id));
        Assert.Equal(
            AgentHarness.OrchestratorKickoff,
            File.ReadAllText(Path.Combine(reply.Value!.Folder, ".fleet", AgentHarness.AgentInstructionFile)));
    }

    [Fact]
    public async Task With_main_on_and_subs_off_the_sub_runs_bare_claude_and_the_kickoff_is_typed_in()
    {
        var handler = new DispatchHandler(
            _mux, _store, new NullHarnessConfig(), TimeSpan.Zero, TimeSpan.Zero,
            settings: new FakeSettingsStore(SettingsConfig.Default
                .WithMainOrchestratorInNvim(true)
                .WithSubOrchestratorsInNvim(false)));

        var reply = await handler.HandleAsync(Command("do the thing"), "t");

        Assert.True(reply.Succeeded, reply.Error);

        var pane = Assert.Single(await _mux.ListPanesAsync());
        Assert.Equal(
            [AgentHarness.Claude, "--name", "techweb-sub-do-the-thing", "--model", "sonnet", "--effort", "medium"],
            _mux.ArgsFor(pane.Id));
        Assert.Equal(AgentHarness.SessionPersistence, _mux.EnvFor(pane.Id));
        Assert.Equal(reply.Value!.Folder, pane.Cwd);
        Assert.Equal([AgentHarness.OrchestratorKickoff, "\r"], _mux.SentTo(pane.Id));
    }

    private readonly FakeStarter _starter = new();

    private DispatchHandler Direct(AidlcMode mode = AidlcMode.Off, ISlugNamer? namer = null) =>
        new(
            _mux, _store, new NullHarnessConfig(), TimeSpan.Zero, TimeSpan.Zero,
            namer: namer,
            settings: new FakeSettingsStore(
                SettingsConfig.Default.WithAidlc(AidlcSettings.Default with { Mode = mode })),
            intents: _intents,
            starter: _starter);

    private DispatchCommand ForRepository(
        string prompt, string repository = "backend", string? branch = null, bool research = false) =>
        new("techweb", _root, prompt, "techweb-main", Repository: repository, Branch: branch, Research: research);

    [Fact]
    public async Task With_a_repository_it_starts_a_repo_agent_and_no_sub_orchestrator()
    {
        var reply = await Direct().HandleAsync(ForRepository("fix the login timeout", branch: "fix/login"), "t");

        Assert.True(reply.Succeeded, reply.Error);
        Assert.Equal([new AgentStart("techweb", "backend", "fix/login", "fix the login timeout")], _starter.Started);
        Assert.Empty(_store.Saved);
        Assert.Empty(await _mux.ListPanesAsync());
        Assert.False(Directory.Exists(OrchestrationPaths.Root(_root)));
        Assert.Contains("backend/fix/login", reply.Value!.Note);
    }

    [Fact]
    public async Task Without_a_branch_the_direct_agent_is_named_like_a_sub()
    {
        _store.Saved.Add(new AgentRecord(
            "w", "backend", "fix-login-timeout", AgentHarness.Nvim, "main", RepositoryWasBare: false));

        var reply = await Direct(namer: new FakeSlugNamer(name: "fix login timeout"))
            .HandleAsync(ForRepository("please make the login page stop timing out"), "t");

        Assert.True(reply.Succeeded, reply.Error);
        Assert.Equal("fix-login-timeout-2", _starter.Started.Single().Branch);
    }

    [Fact]
    public async Task A_derived_branch_skips_a_branch_that_already_exists_in_git()
    {
        _starter.Branches.AddRange(["main", "fix-login-timeout"]);

        var reply = await Direct(namer: new FakeSlugNamer(name: "fix login timeout"))
            .HandleAsync(ForRepository("please make the login page stop timing out"), "t");

        Assert.True(reply.Succeeded, reply.Error);
        Assert.Equal("fix-login-timeout-2", _starter.Started.Single().Branch);
    }

    [Fact]
    public async Task A_blank_branch_is_derived_like_a_missing_one()
    {
        var reply = await Direct(namer: new FakeSlugNamer(name: "fix login timeout"))
            .HandleAsync(ForRepository("fix it", branch: "  "), "t");

        Assert.True(reply.Succeeded, reply.Error);
        Assert.Equal("fix-login-timeout", _starter.Started.Single().Branch);
    }

    [Fact]
    public async Task A_repository_left_to_a_sub_is_mentioned_in_the_reply()
    {
        var reply = await Direct(AidlcMode.On).HandleAsync(ForRepository("fix the login timeout"), "t");

        Assert.Contains("repository was not used", reply.Value!.Note);
    }

    [Fact]
    public async Task Without_a_repository_the_reply_says_nothing_about_one()
    {
        var reply = await Direct().HandleAsync(Command("fix the login timeout"), "t");

        Assert.DoesNotContain("repository was not used", reply.Value!.Note);
    }

    [Fact]
    public async Task Without_a_repository_the_starter_is_never_used()
    {
        var reply = await Direct().HandleAsync(Command("fix the login timeout"), "t");

        Assert.True(reply.Succeeded, reply.Error);
        Assert.Empty(_starter.Started);
        Assert.Single(_store.Saved, a => AgentHarness.IsOrchestrator(a.Harness));
    }

    [Fact]
    public async Task With_aidlc_on_a_repository_still_gets_a_sub_orchestrator()
    {
        var reply = await Direct(AidlcMode.On).HandleAsync(ForRepository("fix the login timeout"), "t");

        Assert.True(reply.Succeeded, reply.Error);
        Assert.Empty(_starter.Started);
        Assert.Contains("## Process", InstructionsOf(reply.Value!));
    }

    [Fact]
    public async Task A_failed_start_is_the_dispatch_error()
    {
        _starter.Fails = "No repository named 'backend' in this project.";

        var reply = await Direct().HandleAsync(ForRepository("fix it"), "t");

        Assert.False(reply.Succeeded);
        Assert.Equal(_starter.Fails, reply.Error);
    }

    [Fact]
    public async Task Without_a_starter_a_repository_is_refused_rather_than_ignored()
    {
        var reply = await Handler.HandleAsync(ForRepository("fix it"), "t");

        Assert.False(reply.Succeeded);
        Assert.Equal(DispatchNote.NoDirect, reply.Error);
        Assert.Empty(_store.Saved);
    }

    [Fact]
    public async Task Research_gets_a_sub_with_the_research_brief_even_with_a_repository()
    {
        var reply = await Direct().HandleAsync(ForRepository("compare caching options", research: true), "t");

        Assert.True(reply.Succeeded, reply.Error);
        Assert.Empty(_starter.Started);
        Assert.Contains(OrchestrationText.Research.Trim(), InstructionsOf(reply.Value!));
    }

    [Fact]
    public async Task The_research_profile_gets_the_research_brief()
    {
        var reply = await Direct(AidlcMode.Manual).HandleAsync(Command("research: compare caching options"), "t");

        Assert.True(reply.Succeeded, reply.Error);
        Assert.Contains(OrchestrationText.Research.Trim(), InstructionsOf(reply.Value!));
    }

    [Fact]
    public async Task Other_dispatches_get_no_research_brief()
    {
        var reply = await Direct(AidlcMode.Manual).HandleAsync(Command("feature: add oauth"), "t");

        Assert.DoesNotContain(OrchestrationText.Research.Trim(), InstructionsOf(reply.Value!));
    }

    private sealed class FakeStarter : IAgentStarter
    {
        public List<AgentStart> Started { get; } = [];

        public string? Fails { get; set; }

        public List<string> Branches { get; } = [];

        public Task<IReadOnlyCollection<string>> BranchesAsync(
            string project, string repository, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyCollection<string>>(Branches);

        public Task<Result<string>> StartAsync(AgentStart request, CancellationToken ct = default)
        {
            Started.Add(request);

            return Task.FromResult(
                Fails is { } reason ? Result<string>.Fail(reason) : Result<string>.Ok("started."));
        }
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

    [Fact]
    public async Task A_sub_orchestrator_brief_tells_it_to_research_with_subagents_by_default()
    {
        var reply = await Handler.HandleAsync(Command("check the api"), "t");

        var instructions = InstructionsOf(reply.Value!);

        Assert.Contains("## Subagents", instructions);
        Assert.Contains(SubagentGuidance.ForSubOrchestrators.Trim(), instructions);
    }

    [Fact]
    public async Task With_subagent_guidance_off_the_sub_orchestrator_brief_leaves_it_out()
    {
        var handler = new DispatchHandler(
            _mux, _store, new NullHarnessConfig(), TimeSpan.Zero, TimeSpan.Zero,
            settings: new FakeSettingsStore(SettingsConfig.Default.WithSubagentGuidance(false)));

        var reply = await handler.HandleAsync(Command("check the api"), "t");

        Assert.DoesNotContain("## Subagents", InstructionsOf(reply.Value!));
    }

    [Fact]
    public async Task A_project_how_you_work_override_keeps_the_subagent_paragraph()
    {
        Directory.CreateDirectory(ProjectConfigPaths.Root(_root));
        File.WriteAllText(ProjectConfigPaths.InstructionsFile(_root), "Only ever touch the api/ folder.");

        var reply = await Handler.HandleAsync(Command("start work"), "t");

        Assert.Contains(SubagentGuidance.ForSubOrchestrators.Trim(), InstructionsOf(reply.Value!));
    }
}
