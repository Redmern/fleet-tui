using Fleet.Features.Agents.ListAgents;
using Fleet.Features.Agents.ListAgents.Models;
using Fleet.Ports.Agents.Models;
using Fleet.Shared.Constants;

namespace Fleet.Tests.Features.Agents;

public class AgentSplitTests
{
    private static AgentRecord Agent(string branch, bool hidden) =>
        new($"C:/repos/techweb/backend/{branch}", "backend", branch, AgentHarness.Claude,
            "main", true, hidden);

    [Fact]
    public void Open_and_hidden_agents_land_on_their_own_tab()
    {
        var listing = AgentSplit.By([Agent("dev", false), Agent("test", true)]);

        Assert.Equal(["dev"], listing.Open.Select(a => a.Branch));
        Assert.Equal(["test"], listing.Hidden.Select(a => a.Branch));
    }

    [Fact]
    public void The_tab_index_picks_the_right_group()
    {
        var listing = AgentSplit.By([Agent("dev", false), Agent("test", true)]);

        Assert.Equal("dev", listing.For(AgentListing.OpenTab)[0].Branch);
        Assert.Equal("test", listing.For(AgentListing.HiddenTab)[0].Branch);
    }

    [Fact]
    public void No_agents_gives_two_empty_groups()
    {
        var listing = AgentSplit.By([]);

        Assert.Empty(listing.Open);
        Assert.Empty(listing.Hidden);
    }

    [Fact]
    public void Order_within_a_group_is_preserved()
    {
        var listing = AgentSplit.By([Agent("b", false), Agent("a", false)]);

        Assert.Equal(["b", "a"], listing.Open.Select(a => a.Branch));
    }
}
