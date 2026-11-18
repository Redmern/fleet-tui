using Fleet.Features.Orchestrations.SetProfile;
using Fleet.Platform.Aidlc;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Aidlc;
using Fleet.Ports.Settings;
using Fleet.Shared.Aidlc;
using Fleet.Shared.Aidlc.Enums;
using Fleet.Shared.Aidlc.Models;
using Fleet.Shared.Constants;
using Fleet.Shared.Harness;
using Fleet.Shared.Orchestrations;
using Fleet.Shared.Orchestrations.Models;
using Fleet.Shared.Settings.Models;
using Fleet.Tests.EndToEnd.MixedTeam;

namespace Fleet.Tests.Features.Orchestrations;

// AC-15: set_profile changes the orchestration's profile in state.json, regenerates the stages not started yet,
// appends ProfileSet to audit.jsonl and rewrites the folder's instruction file. Real JsonIntentStore on a temp folder.
public sealed class SetProfileTests : IDisposable
{
    private const string Project = "techweb";

    private const string Slug = "add-endpoint";

    private const string Stamp = "2026-11-17T10:00:00.0000000+00:00";

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    private readonly MixedTeamRig.MemoryStore _store = new();

    private readonly JsonIntentStore _intents = new();

    private readonly Settings _settings = new();

    private string Folder => OrchestrationPaths.For(_root, Slug);

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

    private SetProfileHandler Handler => new(_store, _intents, _settings);

    private void Dispatched(
        Profile profile = Profile.Feature,
        Func<Stage, StageState>? state = null,
        string orchestratorHarness = "",
        AidlcPart off = AidlcPart.None,
        bool research = false)
    {
        var plan = ProcessPlan.Resolve(profile, Autonomy.Guided, off);
        var intake = Intake.Start(Slug, plan, ProfileSource.ProjectDefault, "t0");
        var stages = intake.State.Stages
            .Select(s => state is null || s.Stage == Stage.Intake ? s : s with { State = state(s.Stage) })
            .ToList();

        Directory.CreateDirectory(Folder);
        _intents.Save(Folder, intake.State with { Stages = stages });

        foreach (var entry in intake.Events)
        {
            _intents.Append(Folder, entry);
        }

        var record = new AgentRecord(
            Folder, string.Empty, Slug, AgentHarness.Orchestrator, string.Empty, false,
            OrchestratorHarness: orchestratorHarness);
        _store.Save(Project, record);

        File.WriteAllText(
            OrchestrationPaths.InstructionsFile(Folder, record.OrchestratorProfile),
            OrchestrationText.Instructions(
                new OrchestrationBrief(Project, Slug, "Add an endpoint", "t0"),
                "Custom rules.",
                ProcessText.Render(plan),
                research || profile == Profile.Research,
                nativeMessaging: record.OrchestratorProfile.Has(HarnessCapabilities.NativeMessaging)));
    }

    private static StageState Started(Stage stage) => stage switch
    {
        Stage.Discover => StageState.Done,
        Stage.Specify => StageState.Awaiting,
        _ => StageState.Pending,
    };

    private IntentState State => _intents.Load(Folder)!;

    private string Instructions(string harness = HarnessRegistry.ClaudeName) =>
        File.ReadAllText(OrchestrationPaths.InstructionsFile(Folder, HarnessRegistry.For(harness)));

    [Fact]
    public void The_stages_already_started_are_kept_and_the_ones_not_started_follow_the_new_profile()
    {
        Dispatched(Profile.Feature, Started);

        var set = Handler.Handle(Project, _root, Slug, "bugfix", Stamp);

        Assert.True(set.Succeeded, set.Error);
        Assert.Equal(Profile.Bugfix, State.Profile);
        Assert.Equal(Stamp, State.Updated);
        Assert.Equal(
            [
                new StagePlan(Stage.Intake, false, StageState.Done),
                new StagePlan(Stage.Discover, false, StageState.Done),
                new StagePlan(Stage.Specify, true, StageState.Awaiting),
                new StagePlan(Stage.Build, false, StageState.Pending),
                new StagePlan(Stage.Verify, false, StageState.Pending),
                new StagePlan(Stage.Review, false, StageState.Pending),
                new StagePlan(Stage.Deliver, true, StageState.Pending),
            ],
            State.Stages);
    }

    [Fact]
    public void A_finished_stage_the_new_profile_does_not_have_is_kept()
    {
        Dispatched(Profile.Feature, stage => stage is Stage.Specify ? StageState.Done : StageState.Pending);

        Assert.True(Handler.Handle(Project, _root, Slug, "research", Stamp).Succeeded);

        Assert.Equal(
            [Stage.Intake, Stage.Discover, Stage.Specify, Stage.Deliver],
            State.Stages.Select(s => s.Stage));
        Assert.Equal(StageState.Done, State.Stages.Single(s => s.Stage == Stage.Specify).State);
    }

    [Fact]
    public void The_new_stages_follow_the_parts_switched_off_in_settings_now()
    {
        Dispatched(Profile.Express);
        _settings.Config = SettingsConfig.Default.WithAidlc(
            SettingsConfig.Default.Aidlc with { Off = AidlcPart.Review | AidlcPart.DeliverGate });

        Assert.True(Handler.Handle(Project, _root, Slug, "bugfix", Stamp).Succeeded);

        Assert.Equal(
            new StagePlan(Stage.Review, false, StageState.Skipped, ProcessPlan.OffInSettings),
            State.Stages.Single(s => s.Stage == Stage.Review));
        Assert.False(State.Stages.Single(s => s.Stage == Stage.Deliver).HumanGate);
    }

    [Fact]
    public void A_profile_set_event_with_the_old_and_new_profile_is_appended_to_the_audit()
    {
        Dispatched(Profile.Feature);
        var before = _intents.Audit(Folder).Count;

        Assert.True(Handler.Handle(Project, _root, Slug, "Research", Stamp).Succeeded);

        var audit = _intents.Audit(Folder);
        Assert.Equal(before + 1, audit.Count);
        Assert.Equal(
            new AuditEntry(Stamp, AuditActor.Conductor, AuditEvent.ProfileSet, Detail: "research (was feature)"),
            audit[^1]);
        Assert.Contains("\"ProfileSet\"", File.ReadAllLines(OrchestrationPaths.AuditFile(Folder))[^1]);
    }

    [Theory]
    [InlineData("hotfix")]
    [InlineData("")]
    [InlineData("1")]
    public void An_unknown_profile_is_refused_with_the_valid_ones_and_nothing_changes(string profile)
    {
        Dispatched(Profile.Feature);
        var state = File.ReadAllText(OrchestrationPaths.StateFile(Folder));
        var audit = File.ReadAllText(OrchestrationPaths.AuditFile(Folder));
        var instructions = Instructions();

        var set = Handler.Handle(Project, _root, Slug, profile, Stamp);

        Assert.False(set.Succeeded);
        Assert.Contains("express, bugfix, feature, refactor, research", set.Error);
        Assert.Equal(state, File.ReadAllText(OrchestrationPaths.StateFile(Folder)));
        Assert.Equal(audit, File.ReadAllText(OrchestrationPaths.AuditFile(Folder)));
        Assert.Equal(instructions, Instructions());
    }

    [Theory]
    [InlineData("")]
    [InlineData("agent:api/feat")]
    [InlineData("other-sub")]
    public void A_caller_that_is_not_a_sub_orchestrator_is_refused(string caller)
    {
        Dispatched(Profile.Feature);
        _store.Save(Project, new AgentRecord(Path.Combine(_root, "api"), "api", "feat", AgentHarness.Claude, "", false));
        var state = File.ReadAllText(OrchestrationPaths.StateFile(Folder));

        var set = Handler.Handle(Project, _root, caller, "bugfix", Stamp);

        Assert.False(set.Succeeded);
        Assert.Contains("only a sub-orchestrator", set.Error);
        Assert.Equal(state, File.ReadAllText(OrchestrationPaths.StateFile(Folder)));
    }

    [Fact]
    public void A_sub_orchestrator_without_ai_dlc_state_is_told_so()
    {
        Dispatched(Profile.Feature);
        File.Delete(OrchestrationPaths.StateFile(Folder));

        var set = Handler.Handle(Project, _root, Slug, "bugfix", Stamp);

        Assert.False(set.Succeeded);
        Assert.Contains("state.json", set.Error);
    }

    [Fact]
    public void The_same_profile_changes_nothing()
    {
        Dispatched(Profile.Feature, Started);
        var state = File.ReadAllText(OrchestrationPaths.StateFile(Folder));
        var audit = File.ReadAllText(OrchestrationPaths.AuditFile(Folder));

        var set = Handler.Handle(Project, _root, Slug, "feature", Stamp);

        Assert.True(set.Succeeded, set.Error);
        Assert.Contains("already", set.Value);
        Assert.Equal(state, File.ReadAllText(OrchestrationPaths.StateFile(Folder)));
        Assert.Equal(audit, File.ReadAllText(OrchestrationPaths.AuditFile(Folder)));
    }

    [Fact]
    public void The_instruction_file_is_rewritten_with_the_new_stage_text_and_the_rest_kept()
    {
        Dispatched(Profile.Feature, Started);

        var set = Handler.Handle(Project, _root, Slug, "bugfix", Stamp);

        var text = Instructions();
        Assert.Contains("**bugfix** profile", text);
        Assert.DoesNotContain("**feature** profile", text);
        Assert.Contains("**Discover**", text);
        Assert.DoesNotContain("**Plan**", text);
        Assert.Contains("# Sub-orchestrator: add-endpoint", text);
        Assert.Contains("Custom rules.", text);
        Assert.Contains("## Reporting", text);
        Assert.Contains("CLAUDE.md", set.Value);
    }

    [Fact]
    public void The_rewritten_process_keeps_the_projects_process_guidance()
    {
        Directory.CreateDirectory(ProjectConfigPaths.Root(_root));
        File.WriteAllText(ProjectConfigPaths.AidlcFile(_root), "Always run the e2e suite.");
        Dispatched(Profile.Feature);

        Assert.True(Handler.Handle(Project, _root, Slug, "bugfix", Stamp).Succeeded);

        Assert.Contains("### Project guidance\nAlways run the e2e suite.", Instructions().ReplaceLineEndings("\n"));
    }

    [Fact]
    public void A_sub_running_another_harness_gets_its_own_instruction_file_rewritten()
    {
        Dispatched(Profile.Feature, orchestratorHarness: HarnessRegistry.OpencodeName);

        var set = Handler.Handle(Project, _root, Slug, "express", Stamp);

        Assert.True(set.Succeeded, set.Error);
        Assert.Contains("**express** profile", Instructions(HarnessRegistry.OpencodeName));
        Assert.Contains("AGENTS.md", set.Value);
    }

    [Fact]
    public void Without_an_instruction_file_the_state_still_changes_and_no_file_is_made()
    {
        Dispatched(Profile.Feature);
        File.Delete(OrchestrationPaths.InstructionsFile(Folder));

        var set = Handler.Handle(Project, _root, Slug, "bugfix", Stamp);

        Assert.True(set.Succeeded, set.Error);
        Assert.Equal(Profile.Bugfix, State.Profile);
        Assert.Contains("could not be rewritten", set.Value);
        Assert.False(File.Exists(OrchestrationPaths.InstructionsFile(Folder)));
    }

    [Fact]
    public void Switching_to_research_adds_the_no_agents_section_and_drops_the_waiting_section()
    {
        Dispatched(Profile.Feature);

        Assert.True(Handler.Handle(Project, _root, Slug, "research", Stamp).Succeeded);

        Assert.Contains("## Research", Instructions());
        Assert.DoesNotContain("## Waiting for agents", Instructions());
    }

    [Fact]
    public void Switching_away_from_research_drops_the_no_agents_section_and_adds_the_waiting_section()
    {
        Dispatched(Profile.Research, orchestratorHarness: HarnessRegistry.OpencodeName);

        Assert.True(Handler.Handle(Project, _root, Slug, "bugfix", Stamp).Succeeded);

        var text = Instructions(HarnessRegistry.OpencodeName);
        Assert.DoesNotContain("## Research", text);
        Assert.Contains(OrchestrationText.WaitingForAgentsTyped.Trim(), text);
    }

    [Fact]
    public void A_sub_dispatched_as_research_on_another_profile_stays_research()
    {
        Dispatched(Profile.Feature, research: true);

        Assert.True(Handler.Handle(Project, _root, Slug, "bugfix", Stamp).Succeeded);

        Assert.Contains("## Research", Instructions());
        Assert.DoesNotContain("## Waiting for agents", Instructions());
    }

    [Fact]
    public void A_stage_skipped_by_settings_at_dispatch_follows_the_settings_now()
    {
        Dispatched(Profile.Express, off: AidlcPart.Review);

        Assert.True(Handler.Handle(Project, _root, Slug, "bugfix", Stamp).Succeeded);

        Assert.Equal(StageState.Pending, State.Stages.Single(s => s.Stage == Stage.Review).State);
    }

    [Fact]
    public void The_reply_tells_the_sub_to_carry_on_from_the_stage_it_is_in()
    {
        Dispatched(Profile.Feature);

        var set = Handler.Handle(Project, _root, Slug, "bugfix", Stamp);

        Assert.Contains("carry on from the stage you are in", set.Value);
        Assert.Contains("keep the artifacts and approvals of the stages you finished", set.Value);
    }

    [Fact]
    public void A_state_file_that_cannot_be_written_is_refused_and_nothing_is_logged()
    {
        Dispatched(Profile.Feature);
        var audit = File.ReadAllText(OrchestrationPaths.AuditFile(Folder));
        var instructions = Instructions();

        var set = new SetProfileHandler(_store, new Unwritable(_intents), _settings)
            .Handle(Project, _root, Slug, "bugfix", Stamp);

        Assert.False(set.Succeeded);
        Assert.Contains("could not write", set.Error);
        Assert.Equal(audit, File.ReadAllText(OrchestrationPaths.AuditFile(Folder)));
        Assert.Equal(instructions, Instructions());
    }

    private sealed class Unwritable(IIntentStore inner) : IIntentStore
    {
        public IntentState? Load(string folder) => inner.Load(folder);

        public void Save(string folder, IntentState state) => throw new IOException("locked");

        public void Append(string folder, AuditEntry entry) => inner.Append(folder, entry);

        public IReadOnlyList<AuditEntry> Audit(string folder) => inner.Audit(folder);
    }

    private sealed class Settings : ISettingsStore
    {
        public SettingsConfig Config { get; set; } = SettingsConfig.Default;

        public SettingsConfig Load(string project) => Config;

        public void Save(string project, SettingsConfig config) => Config = config;
    }
}
