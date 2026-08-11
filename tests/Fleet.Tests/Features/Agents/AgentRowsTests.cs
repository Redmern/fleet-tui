using Fleet.Features.Agents.ListAgents;
using Fleet.Ports.Agents.Models;

namespace Fleet.Tests.Features.Agents;

public class AgentRowsTests
{
    private static AgentRecord Agent(string repo, string branch) =>
        new($"C:/repos/{repo}/{branch}", repo, branch, "claude", "origin/main", true);

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
}
