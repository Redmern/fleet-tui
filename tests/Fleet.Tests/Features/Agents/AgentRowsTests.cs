using Fleet.Features.Agents.ListAgents;
using Fleet.Ports.Agents.Models;
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
        Assert.Equal(rows[0].Trailing![0], rows[1].Trailing![0]);
        Assert.Equal(Width(rows[0].Trailing!), Width(rows[1].Trailing!));
    }

    private static int Width(IEnumerable<FleetSpan> spans) => spans.Sum(s => s.Text.EnumerateRunes().Count());

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

        Assert.Equal([Fleet.Ui.FleetHiddenMark.Blank], rows[0].Trailing!);
    }
}
