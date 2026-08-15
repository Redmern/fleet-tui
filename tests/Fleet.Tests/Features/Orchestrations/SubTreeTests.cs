using Fleet.Features.Orchestrations.ListSubs;
using Fleet.Ports.Agents.Models;
using Fleet.Shared.Constants;

namespace Fleet.Tests.Features.Orchestrations;

public sealed class SubTreeTests
{
    private static AgentRecord Orchestrator(string slug, string status = "") =>
        new(
            $"C:/repos/techweb/.fleet/orchestrations/{slug}",
            string.Empty,
            slug,
            AgentHarness.Orchestrator,
            "origin/main",
            false,
            Hidden: true,
            Open: true,
            Owner: "",
            Status: status);

    private static AgentRecord Agent(string repo, string branch, string owner = "") =>
        new(
            $"C:/repos/techweb/{repo}/{branch}",
            repo,
            branch,
            AgentHarness.Claude,
            "origin/main",
            true,
            Owner: owner);

    [Fact]
    public void Top_level_agents_without_an_owner_stay_on_the_agents_board()
    {
        var listing = SubTree.Of([Agent("backend", "login"), Orchestrator("upgrade")]);

        Assert.Equal(["login"], listing.Board.Select(a => a.Branch));
    }

    [Fact]
    public void Each_orchestrator_is_followed_by_its_own_children_in_the_flat_list()
    {
        var listing = SubTree.Of(
        [
            Agent("backend", "story", owner: "upgrade"),
            Orchestrator("upgrade"),
            Agent("frontend", "form", owner: "upgrade"),
        ]);

        Assert.Equal(
            [("upgrade", false), ("story", true), ("form", true)],
            listing.Flat.Select(e => (e.Agent.Branch, e.IsChild)));
    }

    [Fact]
    public void Children_never_appear_on_the_agents_board()
    {
        var listing = SubTree.Of(
        [
            Orchestrator("upgrade"),
            Agent("backend", "story", owner: "upgrade"),
        ]);

        Assert.Empty(listing.Board);
    }

    [Fact]
    public void Orchestrators_are_ordered_by_slug()
    {
        var listing = SubTree.Of([Orchestrator("zebra"), Orchestrator("alpha")]);

        Assert.Equal(
            ["alpha", "zebra"],
            listing.Flat.Where(e => !e.IsChild).Select(e => e.Agent.Branch));
    }

    [Fact]
    public void A_child_whose_owner_vanished_is_still_listed_rather_than_lost()
    {
        var listing = SubTree.Of([Agent("backend", "story", owner: "gone-slug")]);

        var orphan = Assert.Single(listing.Flat);

        Assert.True(orphan.IsChild);
        Assert.Equal("story", orphan.Agent.Branch);
        Assert.Empty(listing.Board);
    }
}
