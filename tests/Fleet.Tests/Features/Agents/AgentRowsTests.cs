using Fleet.Features.Agents.ListAgents;
using Fleet.Ports.Agents.Models;
using Fleet.Shared.Evidence;
using Fleet.Ui.Constants;
using Fleet.Ui.Models;

namespace Fleet.Tests.Features.Agents;

public class AgentRowsTests
{
    private static AgentRecord Agent(string repo, string branch) =>
        new($"C:/repos/{repo}/{branch}", repo, branch, "claude", "origin/main", true);

    [Fact]
    public void A_hidden_and_a_visible_agent_put_their_status_in_the_same_column()
    {
        var rows = AgentRows.For(
            [Agent("backend", "a") with { Status = "working", Hidden = true }, Agent("backend", "b") with { Status = "working" }]);

        Assert.Contains(FleetGlyphs.Hidden, rows[0].Text);
        Assert.DoesNotContain(FleetGlyphs.Hidden, rows[1].Text);
        Assert.Contains(FleetIcons.Show, rows[1].Text);
        Assert.DoesNotContain(FleetIcons.Show, rows[0].Text);
        Assert.Equal(rows[0].Trailing![0], rows[1].Trailing![0]);
        Assert.Equal(Width(rows[0].Trailing!), Width(rows[1].Trailing!));
    }

    private static int Width(IEnumerable<FleetSpan> spans) => spans.Sum(s => s.Text.EnumerateRunes().Count());

    private static ReportEvidence Evidence(int? exit = null) =>
        new("/w", "main", "aaaaaaaaaa", "bbbbbbbcccccccc", false, exit is null ? string.Empty : "t", exit);

    private static IReadOnlyList<FleetSpan> Spans(AgentRecord agent) =>
        AgentRows.For([agent])[0].Spans;

    [Fact]
    public void A_done_agent_shows_its_short_head_and_a_verified_badge()
    {
        var spans = Spans(Agent("backend", "a") with { Status = "done", Evidence = Evidence(exit: 0) });

        Assert.Contains(spans, s => s.Text.Trim() == "bbbbbbb");
        Assert.Contains(spans, s => s.Text.Trim() == "verified" && s.Tone == FleetTones.Good);
    }

    [Fact]
    public void A_done_agent_with_a_failing_verify_shows_a_failed_badge()
    {
        var spans = Spans(Agent("backend", "a") with { Status = "done", Evidence = Evidence(exit: 1) });

        Assert.Contains(spans, s => s.Text.Trim() == "verify failed" && s.Tone == FleetTones.Bad);
    }

    [Fact]
    public void A_done_agent_without_verify_fields_shows_an_unverified_badge()
    {
        var spans = Spans(Agent("backend", "a") with { Status = "done", Evidence = Evidence() });

        Assert.Contains(spans, s => s.Text.Trim() == "unverified" && s.Tone == FleetTones.Warn);
    }

    [Fact]
    public void A_done_agent_from_before_evidence_existed_is_unverified_with_no_head()
    {
        var spans = Spans(Agent("backend", "a") with { Status = "done" });

        Assert.Contains(spans, s => s.Text.Trim() == "unverified");
        Assert.DoesNotContain(spans, s => s.Text.Trim().Length == 7 && s.Text.Trim().All(char.IsAsciiHexDigit));
    }

    [Fact]
    public void A_working_agent_shows_its_head_but_no_verify_badge()
    {
        var spans = Spans(Agent("backend", "a") with { Status = "working", Evidence = Evidence() });

        Assert.Contains(spans, s => s.Text.Trim() == "bbbbbbb");
        Assert.DoesNotContain(spans, s => s.Text.Contains("verif"));
    }

    [Fact]
    public void A_sub_orchestrator_row_is_not_badged_unverified()
    {
        var spans = Spans(Agent("", "upgrade") with { Harness = "orchestrator", Status = "done" });

        Assert.DoesNotContain(spans, s => s.Text.Contains("unverified"));
    }

    [Fact]
    public void No_agents_shows_how_to_start_one()
    {
        var rows = AgentRows.For([]);

        Assert.Equal([AgentRows.EmptyHint], rows.Select(r => r.Text));
    }

    [Fact]
    public void Each_agent_shows_its_branch_and_repository()
    {
        var rows = AgentRows.For([Agent("backend", "feature_login")]);

        Assert.Contains("feature_login", rows[0].Text);
        Assert.Contains("backend", rows[0].Text);
    }

    [Fact]
    public void Columns_are_aligned_on_the_longest_value()
    {
        var rows = AgentRows.For(
            [Agent("backend", "fix"), Agent("a-much-longer-repo", "feature_login")]);

        var repoColumns = rows
            .Select((r, i) => r.Text.IndexOf(i == 0 ? "backend" : "a-much-longer-repo",
                StringComparison.Ordinal));

        Assert.Single(repoColumns.Distinct());
    }

    [Theory]
    [InlineData(AgentActivity.Working, FleetGlyphs.Working, FleetTones.Warn)]
    [InlineData(AgentActivity.Waiting, FleetGlyphs.Waiting, FleetTones.Bad)]
    [InlineData(AgentActivity.Stalled, FleetGlyphs.Stalled, FleetTones.Bad)]
    [InlineData(AgentActivity.Idle, FleetGlyphs.Idle, FleetTones.Warn)]
    [InlineData("done", FleetGlyphs.Done, FleetTones.Good)]
    [InlineData("failed", FleetGlyphs.Failed, FleetTones.Bad)]
    public void Each_status_shows_its_own_coloured_icon(string status, string glyph, string tone)
    {
        var rows = AgentRows.For([Agent("backend", "login") with { Status = status }]);

        var icon = rows[0].Trailing![0];
        Assert.Equal($"{glyph}  ", icon.Text);
        Assert.Equal(tone, icon.Tone);
    }

    [Fact]
    public void An_unknown_status_falls_back_to_a_muted_dot()
    {
        var rows = AgentRows.For([Agent("backend", "login") with { Status = "odd" }]);

        Assert.Equal(new FleetSpan($"{FleetGlyphs.Dirty}  ", FleetTones.Muted), rows[0].Trailing![0]);
    }

    [Fact]
    public void No_status_shows_no_icon()
    {
        var rows = AgentRows.For([Agent("backend", "login")]);

        Assert.Equal([Fleet.Ui.FleetHiddenMark.Visible], rows[0].Trailing!);
    }

    // AC-17
    [Fact]
    public void A_done_agent_with_commits_after_its_report_is_flagged_in_its_row()
    {
        var agent = Agent("backend", "a") with { Status = "done", Evidence = Evidence(exit: 0) };

        var spans = AgentRows.For([agent], _ => new Fleet.Shared.BranchState(1, 0, false, new ReportDrift(3, false)))[0].Spans;

        Assert.Contains(spans, s => s.Text.Trim() == "3 commits after report" && s.Tone == FleetTones.Warn);
    }

    [Fact]
    public void A_rewritten_history_is_flagged_plainly_and_no_drift_shows_nothing()
    {
        var agent = Agent("backend", "a") with { Status = "done", Evidence = Evidence(exit: 0) };

        var rewritten = AgentRows.For([agent], _ => new Fleet.Shared.BranchState(0, 0, false, ReportDrift.HistoryRewritten))[0];
        var level = AgentRows.For([agent], _ => new Fleet.Shared.BranchState(0, 0, false))[0];

        Assert.Contains(rewritten.Spans, s => s.Text.Trim() == "history rewritten since report" && s.Tone == FleetTones.Warn);
        Assert.DoesNotContain("after report", level.Text);
        Assert.DoesNotContain("rewritten", level.Text);
    }
}
