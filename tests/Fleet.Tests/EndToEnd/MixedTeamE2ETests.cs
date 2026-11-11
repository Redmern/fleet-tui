using Fleet.Ports.Mux;
using Fleet.Shared.Constants;
using Fleet.Shared.Harness;
using Fleet.Shared.Mcp;
using Fleet.Shared.Orchestrations;
using Fleet.Shared.Settings.Enums;
using Fleet.Tests.EndToEnd.MixedTeam;

namespace Fleet.Tests.EndToEnd;

// AC-16: a mixed team end to end, in process. A fake mux hosts fake harnesses (FakeHarness); the fleet side is the
// real MCP composition root, one McpActions per caller (see MixedTeamRig for what is real and what is mirrored).
// Each scenario pairs a mixed step with the same step between two claude sessions, so the assertions are the ones
// that differ from claude-to-claude: typed delivery and typed [fleet] notices instead of a SendMessage address.
[Collection(ConfigHomeCollection.Name)]
public sealed class MixedTeamE2ETests : ConfigHomeFixture
{
    private const string Main = "";

    private const string NoticeTail = " (check it with agent_status or list_agents, then review its work).";

    private readonly MixedTeamRig _team;

    public MixedTeamE2ETests() => _team = new MixedTeamRig(Path.Combine(ConfigHome, "project"));

    private static (string, string)[] To(string repository, string branch, string message) =>
        [("repository", repository), ("branch", branch), ("message", message)];

    private static (string, string)[] Agent(string repository, string branch) =>
        [("repository", repository), ("branch", branch)];

    private static (string, string)[] Status(string status, string summary) =>
        [("status", status), ("summary", summary)];

    [Theory]
    [InlineData("opencode")]
    [InlineData("codex")]
    [InlineData("pi")]
    public async Task A_claude_main_runs_a_sub_orchestrator_on_another_harness_that_leads_a_claude_worker(string harness)
    {
        var profile = HarnessRegistry.For(harness);
        var repository = await _team.AddRepositoryAsync("api");

        // dispatch: the main orchestrator (claude) asks for a sub on another harness; subs are hosted in nvim here,
        // which a harness whose typed kickoff is checked must not follow
        _team.SubOrchestratorsInNvim(true);

        var dispatched = await _team.DispatchAsync(Main, "fix the login bug", harness);

        var folder = dispatched.Folder;
        var sub = _team.Mux.HarnessIn(folder);
        await sub.Started;

        Assert.Equal([harness], _team.Mux.ArgsOf(sub));
        Assert.True(File.Exists(Path.Combine(folder, "AGENTS.md")));
        Assert.False(File.Exists(Path.Combine(folder, "CLAUDE.md")));
        Assert.Contains("wait_for_agent", File.ReadAllText(Path.Combine(folder, "AGENTS.md")));
        Assert.Contains("fix the login bug", File.ReadAllText(OrchestrationPaths.TaskFile(folder)));
        Assert.True(File.Exists(Path.Combine(folder, ".agents", "skills", "fl-tdd", "SKILL.md")));

        // the harness found the fleet MCP server in its own project config, as this sub and this project
        Assert.Equal(dispatched.Slug, sub.Caller);
        Assert.Equal(_team.Project, sub.CallerProject);

        // the kickoff was lost once by the starting TUI, typed again, and submitted exactly once
        Assert.Equal(1, sub.LostTypings);
        Assert.Equal([profile.Kickoff, TypedPrompt.ClearLine, profile.Kickoff, "\r"], _team.Mux.TypedInto(sub));
        Assert.Equal([profile.Kickoff], sub.Submitted);

        var record = Assert.Single(_team.Store.List(_team.Project));
        Assert.Equal(harness, record.OrchestratorHarness);
        Assert.Equal(harness, record.RunningHarness);
        Assert.False(record.InNvim);

        if (harness == HarnessRegistry.CodexName)
        {
            Assert.Contains(
                $"[projects.\"{folder}\"]\ntrust_level = \"trusted\"",
                File.ReadAllText(_team.CodexUserConfig).ReplaceLineEndings("\n"));
        }

        if (harness == HarnessRegistry.PiName)
        {
            Assert.Contains(
                File.ReadAllLines(_team.PiTrustStore),
                l => l.Contains(dispatched.Slug, StringComparison.Ordinal)
                     && l.TrimEnd(',').EndsWith("\": true", StringComparison.Ordinal));
        }

        // the sub leads a claude worker that has a cross-session inbox
        var fix = await _team.StartClaudeWorkerAsync(sub.Caller!, "api", repository, "fix");
        var worker = _team.Mux.HarnessIn(fix.Worktree);
        await _team.ClaudeHookReportsInboxAsync(fix, "uds:fix");

        // tell_agent, non-claude -> claude: typed, although the claude worker could take SendMessage
        var told = await _team.CallAsync(sub.Caller!, HarnessTool.TellAgent, To("api", "fix", "find the bug"));

        Assert.Equal("sent to api/fix.", told.Text);
        Assert.Equal([AgentHarness.AgentInstructionPrompt], worker.Submitted);
        Assert.Equal($"find the bug\n\n{AgentHarness.AgentReportRule}", worker.Instruction);

        // the same call from the claude main: a SendMessage address, nothing typed
        var handed = await _team.CallAsync(Main, HarnessTool.TellAgent, To("api", "fix", "also check logout"));

        Assert.Contains("SendMessage", handed.Text);
        Assert.Contains("uds:fix", handed.Text);
        Assert.Single(worker.Submitted);

        // report: the claude worker is done; its owner cannot take SendMessage, so fleet types the notice
        var reported = await _team.CallAsync(
            McpCaller.ForAgent("api", "fix"), HarnessTool.Report, Status("done", "login fixed"));

        Assert.False(reported.IsError, reported.Text);
        Assert.Contains("api/fix: done — login fixed", reported.Text);
        Assert.Contains("Fleet typed this report into your orchestrator's pane", reported.Text);
        Assert.DoesNotContain("SendMessage", reported.Text);
        Assert.Equal("done", _team.Record("api", "fix").Status);
        Assert.Equal("login fixed", _team.Record("api", "fix").Summary);
        Assert.Contains("agent:api/fix: done", _team.DesktopNotifications);

        // orchestrator notification: a [fleet] line submitted in the sub's pane
        Assert.Equal([profile.Kickoff, AgentHarness.AgentInstructionPrompt], sub.Submitted);
        Assert.Equal($"[fleet] api/fix: done — login fixed{NoticeTail}", sub.Instruction);

        // wait_for_agent returns the report
        var waited = await _team.CallAsync(sub.Caller!, HarnessTool.WaitForAgent, Agent("api", "fix"));

        Assert.Equal("api/fix: done — login fixed", waited.Text);

        // the sub reports to the main orchestrator: recorded, desktop notice, shown in list_subs
        var finished = await _team.CallAsync(sub.Caller!, HarnessTool.Report, Status("done", "bug fixed and merged"));

        Assert.False(finished.IsError, finished.Text);
        Assert.Contains($"{dispatched.Slug}: done", _team.DesktopNotifications);

        var subs = await _team.CallAsync(Main, HarnessTool.ListSubs);

        Assert.Contains(dispatched.Slug, subs.Text);
        Assert.Contains("done", subs.Text);
        Assert.Contains("bug fixed and merged", subs.Text);
    }

    [Fact]
    public async Task A_claude_sub_orchestrator_leads_an_opencode_worker_next_to_a_claude_worker()
    {
        var repository = await _team.AddRepositoryAsync("api");

        // dispatch: a claude sub, exactly the claude files
        var dispatched = await _team.DispatchAsync(Main, "ship the login page", string.Empty);
        var lead = dispatched.Slug;
        var sub = _team.Mux.HarnessIn(dispatched.Folder);
        var subRecord = Assert.Single(_team.Store.List(_team.Project));

        Assert.Equal("claude", _team.Mux.ArgsOf(sub)[0]);
        Assert.True(File.Exists(Path.Combine(dispatched.Folder, "CLAUDE.md")));
        Assert.False(File.Exists(Path.Combine(dispatched.Folder, "AGENTS.md")));
        Assert.Equal(string.Empty, subRecord.OrchestratorHarness);
        Assert.Equal([AgentHarness.OrchestratorKickoff], sub.Submitted);
        await _team.ClaudeHookReportsInboxAsync(subRecord, "uds:lead");

        // new_agent with harness opencode, through the real MCP composition root
        var started = await _team.CallAsync(
            lead, HarnessTool.NewAgent,
            ("repository", "api"), ("branch", "feat"), ("harness", "opencode"), ("task", "build the form"));

        Assert.False(started.IsError, started.Text);
        Assert.Contains("started api/feat and gave it its first task.", started.Text);
        Assert.Contains("It runs opencode: tell_agent types into its pane", started.Text);
        Assert.DoesNotContain("Claude session", started.Text);

        var feat = _team.Record("api", "feat");
        var worker = _team.Mux.HarnessIn(feat.Worktree);
        await worker.Started;

        Assert.Equal("opencode", feat.Harness);
        Assert.False(feat.HostInNvim);
        Assert.Equal(lead, feat.Owner);
        Assert.Equal(["opencode"], _team.Mux.ArgsOf(worker));

        // instruction delivery: opencode's own config, skills in its skills dir, no claude files, typed first task
        Assert.Equal("agent:api/feat", worker.Caller);
        Assert.Equal(_team.Project, worker.CallerProject);
        Assert.True(File.Exists(Path.Combine(feat.Worktree, ".agents", "skills", "fl-tdd", "SKILL.md")));
        Assert.False(File.Exists(Path.Combine(feat.Worktree, ".mcp.json")));
        Assert.False(Directory.Exists(Path.Combine(feat.Worktree, ".claude")));
        Assert.Equal(1, worker.LostTypings);
        Assert.Equal([AgentHarness.AgentInstructionPrompt], worker.Submitted);
        Assert.Equal($"build the form\n\n{AgentHarness.AgentReportRule}", worker.Instruction);

        // the opencode worker follows the report rule
        var working = await _team.CallAsync(worker.Caller!, HarnessTool.Report, Status("working", string.Empty));

        Assert.False(working.IsError, working.Text);
        worker.Says("reading the form code");

        // idle: no hooks, so the pane is watched; quiet for the window means idle, and the owner is told
        var dashboard = _team.Dashboard();

        Assert.Equal("working", (await _team.RefreshAsync(dashboard)).Single(a => a.Branch == "feat").Status);

        MixedTeamRig.PaneStaysQuietFor(feat, TimeSpan.FromSeconds(121));

        Assert.Equal("idle", (await _team.RefreshAsync(dashboard)).Single(a => a.Branch == "feat").Status);
        await MixedTeamRig.UntilAsync(() => sub.Submitted.Count == 2, "the idle notice was not typed into the owner");
        Assert.Equal(
            $"[fleet] api/feat: idle — no pane output for 120s and no done report{NoticeTail}", sub.Instruction);

        var listed = await _team.CallAsync(lead, HarnessTool.ListAgents);

        Assert.Contains("api/feat — opencode, hidden [idle]", listed.Text);

        var idle = await _team.CallAsync(lead, HarnessTool.WaitForAgent, Agent("api", "feat"));

        Assert.StartsWith("api/feat: idle — no pane output for 120s and no done report.", idle.Text);

        // tell_agent, claude -> opencode: typed
        var told = await _team.CallAsync(lead, HarnessTool.TellAgent, To("api", "feat", "add validation"));

        Assert.Equal("sent to api/feat.", told.Text);
        Assert.Equal(2, worker.Submitted.Count);
        Assert.Equal($"add validation\n\n{AgentHarness.AgentReportRule}", worker.Instruction);
        Assert.Equal("working", (await _team.RefreshAsync(dashboard)).Single(a => a.Branch == "feat").Status);

        // a claude worker beside it: claude -> claude is still a SendMessage address
        var ui = await _team.StartClaudeWorkerAsync(lead, "api", repository, "ui");
        var peer = _team.Mux.HarnessIn(ui.Worktree);
        await _team.ClaudeHookReportsInboxAsync(ui, "uds:ui");

        var handed = await _team.CallAsync(lead, HarnessTool.TellAgent, To("api", "ui", "style the form"));

        Assert.Contains("SendMessage", handed.Text);
        Assert.Contains("uds:ui", handed.Text);
        Assert.Empty(peer.Submitted);

        // tell_agent, opencode -> claude: typed
        var asked = await _team.CallAsync(worker.Caller!, HarnessTool.TellAgent, To("api", "ui", "which colours?"));

        Assert.Equal("sent to api/ui.", asked.Text);
        Assert.Equal([AgentHarness.AgentInstructionPrompt], peer.Submitted);
        Assert.Equal($"which colours?\n\n{AgentHarness.AgentReportRule}", peer.Instruction);

        // report done from opencode: typed into the claude owner, although that owner has an inbox
        var reported = await _team.CallAsync(worker.Caller!, HarnessTool.Report, Status("done", "form built"));

        Assert.False(reported.IsError, reported.Text);
        Assert.Contains("Fleet typed this report into your orchestrator's pane", reported.Text);
        Assert.DoesNotContain("SendMessage", reported.Text);
        Assert.Equal("done", _team.Record("api", "feat").Status);
        Assert.Contains("agent:api/feat: done", _team.DesktopNotifications);
        Assert.Equal(3, sub.Submitted.Count);
        Assert.Equal($"[fleet] api/feat: done — form built{NoticeTail}", sub.Instruction);

        var waited = await _team.CallAsync(lead, HarnessTool.WaitForAgent, Agent("api", "feat"));

        Assert.Equal("api/feat: done — form built", waited.Text);

        // the claude worker's report to the same claude owner still goes by SendMessage, nothing typed
        var native = await _team.CallAsync(McpCaller.ForAgent("api", "ui"), HarnessTool.Report, Status("done", "styled"));

        Assert.Contains("SendMessage", native.Text);
        Assert.Contains("uds:lead", native.Text);
        Assert.Equal(3, sub.Submitted.Count);
    }
}
