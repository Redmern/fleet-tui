using Fleet.Features.Mcp.ServeMcp;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mcp.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;
using Fleet.Shared.Mcp;
using Fleet.Shared.Settings.Enums;

namespace Fleet.Tests.Features.Mcp;

public sealed class McpHelpersTests
{
    private static McpRequest Request(string tool, params (string, string)[] args) =>
        new(tool, args.ToDictionary(a => a.Item1, a => a.Item2));

    private static AgentRecord Agent(string repo, string branch, bool hidden = false) =>
        new(
            $"C:/repos/techweb/{repo}/{branch}",
            repo,
            branch,
            AgentHarness.Claude,
            "origin/main",
            true,
            Hidden: hidden,
            Open: true);

    [Fact]
    public void SafeText_replaces_control_characters_with_spaces()
    {
        Assert.Equal("a b", SafeText.Clean("a\n\tb"));
    }

    [Fact]
    public void SafeText_truncates_long_values_with_an_ellipsis()
    {
        var cleaned = SafeText.Clean(new string('x', 200), max: 10);

        Assert.Equal(10, cleaned.Length);
        Assert.EndsWith("…", cleaned);
    }

    [Fact]
    public void Missing_arguments_are_named()
    {
        var missing = ToolArguments.Missing(Request("stop_agent", ("repository", "backend")), "repository", "branch");

        Assert.Equal("'branch' is required for stop_agent.", missing);
    }

    [Fact]
    public void All_present_arguments_report_nothing_missing()
    {
        var request = Request("stop_agent", ("repository", "backend"), ("branch", "login"));

        Assert.Null(ToolArguments.Missing(request, "repository", "branch"));
    }

    [Fact]
    public void A_line_count_falls_back_when_unparseable()
    {
        Assert.Equal(200, ToolArguments.Count(Request("log_tail"), "lines", 200));
        Assert.Equal(50, ToolArguments.Count(Request("log_tail", ("lines", "50")), "lines", 200));
    }

    [Fact]
    public void An_agent_is_matched_case_insensitively_by_repository_and_branch()
    {
        var agents = new[] { Agent("backend", "login"), Agent("frontend", "form") };

        Assert.NotNull(AgentKey.Find(agents, "BACKEND", "Login"));
        Assert.Null(AgentKey.Find(agents, "backend", "missing"));
    }

    [Fact]
    public void The_approval_prompt_names_the_caller_the_action_and_the_target()
    {
        var prompt = ApprovalPrompt.For(
            HarnessTool.NewAgent,
            Request("new_agent", ("repository", "backend"), ("branch", "login")),
            caller: "upgrade");

        Assert.Contains("upgrade", prompt);
        Assert.Contains("Start a new agent", prompt);
        Assert.Contains("backend/login", prompt);
    }

    [Fact]
    public void A_dispatch_to_a_repository_shows_the_repository_and_the_task()
    {
        var prompt = ApprovalPrompt.For(
            HarnessTool.Dispatch,
            Request("dispatch", ("message", "fix the login timeout"), ("repository", "backend")),
            caller: "");

        Assert.Contains("backend: fix the login timeout", prompt);
    }

    [Fact]
    public void A_dispatch_without_a_repository_shows_the_task_once()
    {
        var prompt = ApprovalPrompt.For(
            HarnessTool.Dispatch, Request("dispatch", ("message", "fix the login timeout")), caller: "");

        Assert.EndsWith("— fix the login timeout", prompt);
    }

    [Fact]
    public void The_approval_prompt_calls_the_main_orchestrator_out_when_there_is_no_caller()
    {
        var prompt = ApprovalPrompt.For(HarnessTool.NewAgent, Request("new_agent"), caller: "");

        Assert.Contains("main orchestrator", prompt);
    }

    [Fact]
    public void The_approval_prompt_scrubs_a_hostile_branch_name()
    {
        var prompt = ApprovalPrompt.For(
            HarnessTool.RemoveAgent,
            Request("remove_agent", ("repository", "backend"), ("branch", "a\nrm -rf")),
            caller: "");

        Assert.DoesNotContain('\n', prompt);
    }

    [Fact]
    public void The_audit_line_names_the_main_orchestrator_when_there_is_no_caller()
    {
        Assert.Contains("mcp main", McpAudit.Allowed("", HarnessTool.ListAgents));
        Assert.Contains("mcp upgrade", McpAudit.Allowed("upgrade", HarnessTool.ListAgents));
    }

    [Fact]
    public void Listing_agents_hides_the_orchestrators_themselves()
    {
        var agents = new[]
        {
            Agent("backend", "login"),
            Agent(string.Empty, "upgrade") with { Harness = AgentHarness.Orchestrator },
        };

        var text = ToolText.Agents(agents);

        Assert.Contains("backend/login", text);
        Assert.DoesNotContain("upgrade", text);
    }

    [Fact]
    public void Listing_agents_names_what_each_one_runs_and_whether_nvim_hosts_it()
    {
        var agents = new[]
        {
            Agent("backend", "login") with { Harness = AgentHarness.Claude, HostInNvim = true, Open = true, Status = "working" },
            Agent("backend", "search") with { Harness = AgentHarness.Opencode, Open = true },
            Agent("backend", "notes") with { Harness = AgentHarness.Nvim, Open = false },
        };

        Assert.Equal(
            "backend/login — claude in nvim, open [working]\n"
            + "backend/search — opencode, open\n"
            + "backend/notes — nvim, closed",
            ToolText.Agents(agents));
    }

    [Fact]
    public void An_empty_project_says_so_rather_than_returning_nothing()
    {
        Assert.Equal("No agents yet.", ToolText.Agents([]));
        Assert.Equal("No repositories yet.", ToolText.Repositories([]));
    }

    [Fact]
    public void Agent_status_reports_where_it_is_and_its_drift()
    {
        var text = ToolText.Agent(Agent("backend", "login"), new BranchState(2, 0, true));

        Assert.Contains("backend/login", text);
        Assert.Contains("open", text);
        Assert.Contains("2 ahead", text);
        Assert.Contains("dirty", text);
    }

    [Fact]
    public void Agent_status_names_the_base_ref_next_to_the_counts_measured_against_it()
    {
        var text = ToolText.Agent(Agent("backend", "login"), new BranchState(2, 1, false));

        Assert.Equal("backend/login: open, base origin/main: 2 ahead, 1 behind", text);
    }

    [Fact]
    public void An_agent_level_with_its_base_says_in_sync_after_the_base()
    {
        var text = ToolText.Agent(Agent("backend", "login"), new BranchState(0, 0, false));

        Assert.Equal("backend/login: open, base origin/main: in sync", text);
    }

    [Fact]
    public void A_base_that_could_not_be_measured_is_named_with_that_caveat()
    {
        var text = ToolText.Agent(Agent("backend", "login"), new BranchState(1, 0, false), baseMeasured: false);

        Assert.Equal(
            "backend/login: open, base origin/main (not found; counts are against its upstream): 1 ahead", text);
    }

    [Fact]
    public void An_agent_with_no_recorded_base_names_none()
    {
        var text = ToolText.Agent(Agent("backend", "login") with { BaseRef = string.Empty }, new BranchState(1, 0, false));

        Assert.DoesNotContain("base", text);
    }

    private static Fleet.Shared.Evidence.ReportEvidence Evidence(int? exit = null, bool dirty = false) =>
        new(
            "/work/backend/login", "origin/main", "1111111aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "2222222bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", dirty, exit is null ? string.Empty : "dotnet test", exit,
            exit is null ? string.Empty : "Failed: 1");

    [Fact]
    public void Listing_agents_shows_one_line_each_with_the_short_head_and_the_verify_result()
    {
        var agents = new[]
        {
            Agent("backend", "ok") with { Status = "done", Evidence = Evidence(exit: 0) },
            Agent("backend", "bad") with { Status = "done", Evidence = Evidence(exit: 1) },
            Agent("backend", "none") with { Status = "done", Evidence = Evidence() },
            Agent("backend", "failed") with { Status = "failed", Evidence = Evidence(exit: 1) },
            Agent("backend", "busy") with { Status = "working", Evidence = Evidence() },
        };

        var lines = ToolText.Agents(agents).Split('\n');

        Assert.Equal(5, lines.Length);
        Assert.Equal("backend/ok — claude, open [done], HEAD 2222222, verify ok", lines[0]);
        Assert.Equal("backend/bad — claude, open [done (verify failed)], HEAD 2222222, verify failed", lines[1]);
        Assert.Equal("backend/none — claude, open [done (unverified)], HEAD 2222222, verify unverified", lines[2]);
        Assert.Equal("backend/failed — claude, open [failed], HEAD 2222222, verify failed", lines[3]);
        Assert.Equal("backend/busy — claude, open [working], HEAD 2222222", lines[4]);
    }

    [Fact]
    public void Listing_an_agent_that_reported_done_before_evidence_existed_calls_it_unverified()
    {
        var text = ToolText.Agents([Agent("backend", "old") with { Status = "done" }]);

        Assert.Equal("backend/old — claude, open [done (unverified)], verify unverified", text);
    }

    [Fact]
    public void Listing_shows_every_flag_that_applies()
    {
        var text = ToolText.Agents([Agent("backend", "x") with { Status = "done", Evidence = Evidence(dirty: true) }]);

        Assert.Contains("[done (unverified) (dirty)]", text);
    }

    [Fact]
    public void Agent_status_shows_all_the_evidence_fields()
    {
        var agent = Agent("backend", "login") with
        {
            Status = "done",
            Summary = "endpoint added",
            ReportedAt = "2026-11-17T10:00:00.0000000+00:00",
            Evidence = Evidence(exit: 1, dirty: true),
        };

        var text = ToolText.Agent(agent, new BranchState(2, 0, false));

        Assert.StartsWith("backend/login: open, base origin/main: 2 ahead", text);
        Assert.Contains("report: done (verify failed) (dirty) at 2026-11-17T10:00:00.0000000+00:00", text);
        Assert.Contains("summary: endpoint added", text);
        Assert.Contains("worktree: /work/backend/login", text);
        Assert.Contains("base ref: origin/main", text);
        Assert.Contains("merge-base: 1111111aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", text);
        Assert.Contains("HEAD: 2222222bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", text);
        Assert.Contains("worktree dirty: yes", text);
        Assert.Contains("verify: failed", text);
        Assert.Contains("verify command: dotnet test", text);
        Assert.Contains("verify exit code: 1", text);
        Assert.Contains("verify output (last lines):\nFailed: 1", text);
    }

    // AC-17
    [Fact]
    public void Agent_status_flags_the_commits_after_the_done_report_on_its_first_line()
    {
        var agent = Agent("backend", "login") with { Status = "done", Evidence = Evidence(exit: 0) };

        var text = ToolText.Agent(agent, new BranchState(3, 0, false, new Fleet.Shared.Evidence.ReportDrift(2, false)));
        var rewritten = ToolText.Agent(agent, new BranchState(3, 0, false, Fleet.Shared.Evidence.ReportDrift.HistoryRewritten));

        Assert.StartsWith("backend/login: open, base origin/main: 3 ahead; 2 commits after report\n", text);
        Assert.StartsWith("backend/login: open, base origin/main: 3 ahead; history rewritten since report\n", rewritten);
        Assert.DoesNotContain("after report", ToolText.Agent(agent, new BranchState(3, 0, false)));
    }

    [Fact]
    public void Listing_agents_flags_the_commits_after_the_done_report_per_agent()
    {
        var agents = new[]
        {
            Agent("backend", "moved") with { Status = "done", Evidence = Evidence(exit: 0) },
            Agent("backend", "still") with { Status = "done", Evidence = Evidence(exit: 0) },
        };

        var lines = ToolText.Agents(
                agents, a => a.Branch == "moved" ? new Fleet.Shared.Evidence.ReportDrift(1, false) : null)
            .Split('\n');

        Assert.Equal("backend/moved — claude, open [done], HEAD 2222222, verify ok, 1 commit after report", lines[0]);
        Assert.Equal("backend/still — claude, open [done], HEAD 2222222, verify ok", lines[1]);
    }

    [Fact]
    public void Agent_status_for_a_clean_verified_report_says_so()
    {
        var agent = Agent("backend", "login") with { Status = "done", Evidence = Evidence(exit: 0) };

        var text = ToolText.Agent(agent, new BranchState(0, 0, false));

        Assert.Contains("report: done", text);
        Assert.DoesNotContain("report: done (", text);
        Assert.Contains("worktree dirty: no", text);
        Assert.Contains("verify: ok", text);
    }

    [Fact]
    public void Agent_status_for_an_old_record_says_no_evidence_was_captured()
    {
        var text = ToolText.Agent(Agent("backend", "login") with { Status = "done" }, new BranchState(0, 0, false));

        Assert.Contains("report: done (unverified)", text);
        Assert.Contains("evidence: none recorded", text);
        Assert.Contains("verify: unverified", text);
    }

    [Fact]
    public void Agent_status_for_an_agent_that_never_reported_shows_no_report_lines()
    {
        var text = ToolText.Agent(Agent("backend", "login"), new BranchState(0, 0, false));

        Assert.DoesNotContain("report:", text);
        Assert.DoesNotContain("HEAD:", text);
    }

    [Fact]
    public void The_approval_prompt_for_remove_sub_names_the_sub_and_what_it_deletes()
    {
        var prompt = ApprovalPrompt.For(
            HarnessTool.RemoveSub,
            Request("remove_sub", ("slug", "upgrade"), ("delete_folder", "true"), ("remove_agents", "true")),
            caller: "");

        Assert.Contains("Remove a sub-orchestrator — upgrade (deleting its folder, removing its agents)", prompt);
    }

    [Fact]
    public void The_approval_prompt_for_a_plain_remove_sub_has_no_extras()
    {
        var prompt = ApprovalPrompt.For(
            HarnessTool.RemoveSub, Request("remove_sub", ("slug", "upgrade")), caller: "");

        Assert.EndsWith("— upgrade", prompt);
    }
}
