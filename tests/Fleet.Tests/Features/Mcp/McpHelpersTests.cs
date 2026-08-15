using Fleet.Features.Mcp.ServeMcp;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mcp.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;
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
}
