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

        Assert.Equal([AgentRows.EmptyHint], rows);
    }

    [Fact]
    public void Each_agent_shows_its_repository_branch_and_harness()
    {
        var rows = AgentRows.For([Agent("backend", "feature_login")]);

        Assert.Contains("backend", rows[0]);
        Assert.Contains("feature_login", rows[0]);
        Assert.Contains("claude", rows[0]);
    }

    [Fact]
    public void Columns_are_aligned_on_the_longest_value()
    {
        var rows = AgentRows.For(
            [Agent("backend", "fix"), Agent("a-much-longer-repo", "feature_login")]);

        var harnessColumns = rows.Select(r => r.IndexOf("claude", StringComparison.Ordinal));

        Assert.Single(harnessColumns.Distinct());
    }
}
