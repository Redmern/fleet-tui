using Fleet.Features.Orchestrations.ListSubs;
using Fleet.Ports.Agents.Models;
using Fleet.Shared.Constants;

namespace Fleet.Tests.Features.Orchestrations;

public sealed class SubSummaryTests
{
    private static AgentRecord Sub(string slug, string status = "", string summary = "", string at = "") =>
        new(
            $"C:/repos/techweb/.fleet/orchestrations/{slug}",
            string.Empty,
            slug,
            AgentHarness.Orchestrator,
            "origin/main",
            false,
            Status: status,
            Summary: summary,
            ReportedAt: at);

    private static AgentRecord Agent(string repo, string branch, string owner = "", string status = "") =>
        new($"C:/repos/techweb/{repo}/{branch}", repo, branch, AgentHarness.Nvim, "origin/main", true,
            Owner: owner, Status: status);

    private static string Text(IReadOnlyList<AgentRecord> agents, params string[] open) =>
        SubSummary.Text(agents, a => open.Contains(a.Branch));

    [Fact]
    public void No_subs_says_so_even_with_top_level_agents()
    {
        Assert.Equal(SubSummary.None, Text([Agent("backend", "login")]));
    }

    [Fact]
    public void A_sub_shows_its_status_and_whether_its_pane_is_open()
    {
        var text = Text([Sub("upgrade", OrchestrationStatus.Done), Sub("audit")], "audit");

        Assert.Contains("upgrade — done, pane closed", text);
        Assert.Contains("audit — working, pane open", text);
    }

    [Fact]
    public void A_working_sub_whose_pane_was_stopped_shows_stopped()
    {
        var text = Text([Sub("upgrade", OrchestrationStatus.Working), Sub("audit", OrchestrationStatus.Working)], "audit");

        Assert.Contains("upgrade — stopped, pane closed", text);
        Assert.Contains("audit — working, pane open", text);
    }

    [Fact]
    public void A_done_sub_shows_idle_until_its_agents_finish_and_failed_if_one_failed()
    {
        var waiting = Text([Sub("upgrade", OrchestrationStatus.Done), Agent("backend", "story", "upgrade")]);
        var failed = Text(
        [
            Sub("upgrade", OrchestrationStatus.Done),
            Agent("backend", "story", "upgrade", OrchestrationStatus.Done),
            Agent("frontend", "form", "upgrade", OrchestrationStatus.Failed),
        ]);

        Assert.StartsWith("upgrade — idle, pane closed", waiting, StringComparison.Ordinal);
        Assert.StartsWith("upgrade — failed, pane closed", failed, StringComparison.Ordinal);
    }

    [Fact]
    public void The_last_report_shows_its_time_and_summary()
    {
        var text = Text([Sub("upgrade", OrchestrationStatus.Failed, "tests red", "2026-10-03T10:00:00.0000000+00:00")]);

        Assert.Contains("last report 2026-10-03T10:00:00.0000000+00:00: tests red", text);
    }

    [Fact]
    public void A_sub_that_never_reported_has_no_report_line()
    {
        Assert.DoesNotContain("last report", Text([Sub("upgrade")]));
    }

    [Fact]
    public void Each_sub_lists_its_own_agents_with_their_status()
    {
        var text = Text(
        [
            Sub("upgrade"),
            Agent("backend", "story", "upgrade", OrchestrationStatus.Done),
            Agent("frontend", "form", "upgrade"),
            Agent("backend", "solo"),
        ], "form");

        var lines = text.Split('\n');

        Assert.Equal("upgrade — stopped, pane closed", lines[0]);
        Assert.Contains("  - backend/story — closed, done", lines);
        Assert.Contains("  - frontend/form — open, no report", lines);
        Assert.DoesNotContain("solo", text);
    }

    [Fact]
    public void An_agent_that_reported_shows_its_last_report_under_its_line()
    {
        var reported = Agent("backend", "story", "upgrade", OrchestrationStatus.Done) with
        {
            Summary = "story merged",
            ReportedAt = "2026-10-04T09:00:00Z",
        };

        var lines = Text([Sub("upgrade"), reported]).Split('\n');

        Assert.Equal(["  - backend/story — closed, done", "      last report 2026-10-04T09:00:00Z: story merged"], lines[1..]);
    }

    [Fact]
    public void Unowned_lists_only_the_agents_no_sub_started_sorted_by_repository_and_branch()
    {
        var text = SubSummary.Unowned(
            [
                Sub("upgrade"),
                Agent("frontend", "form", "upgrade"),
                Agent("frontend", "solo", status: "idle"),
                Agent("backend", "fix"),
                Agent("backend", "story", "vanished"),
            ],
            a => a.Branch == "solo");

        Assert.Equal("- backend/fix — closed, no report\n- frontend/solo — open, idle", text);
    }

    [Fact]
    public void Unowned_says_so_when_every_agent_belongs_to_a_sub()
    {
        Assert.Equal(SubSummary.NoUnowned, SubSummary.Unowned([Sub("upgrade"), Agent("backend", "story", "upgrade")], _ => false));
    }

    [Fact]
    public void Agents_whose_sub_is_gone_are_listed_under_its_old_name()
    {
        var text = Text([Sub("upgrade"), Agent("backend", "story", "vanished")]);

        Assert.Contains("vanished — no longer registered", text);
        Assert.Contains("  - backend/story", text);
    }
}
