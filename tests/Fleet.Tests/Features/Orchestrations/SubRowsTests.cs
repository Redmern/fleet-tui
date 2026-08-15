using Fleet.Features.Orchestrations.ListSubs;
using Fleet.Features.Orchestrations.ListSubs.Models;
using Fleet.Ports.Agents.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;
using Fleet.Ui;
using Fleet.Ui.Constants;

namespace Fleet.Tests.Features.Orchestrations;

public sealed class SubRowsTests
{
    private static readonly Func<AgentRecord, BranchState> Clean =
        _ => new BranchState(0, 0, false);

    private static AgentRecord Orchestrator(string slug, string status = "", bool hidden = true) =>
        new(
            $"C:/repos/techweb/.fleet/orchestrations/{slug}",
            string.Empty,
            slug,
            AgentHarness.Orchestrator,
            "origin/main",
            false,
            Hidden: hidden,
            Open: true,
            Status: status);

    private static AgentRecord Agent(string repo, string branch, string owner) =>
        new(
            $"C:/repos/techweb/{repo}/{branch}",
            repo,
            branch,
            AgentHarness.Claude,
            "origin/main",
            true,
            Owner: owner);

    private static SubListing Listing(params AgentRecord[] agents) => SubTree.Of(agents);

    [Fact]
    public void An_empty_listing_shows_the_trigger_hint()
    {
        var rows = SubRows.For(Listing(), Clean, ",");

        Assert.Equal(SubRows.EmptyHint(","), Assert.Single(rows).Text);
    }

    [Fact]
    public void An_orchestrator_row_leads_with_its_glyph_and_slug()
    {
        var rows = SubRows.For(Listing(Orchestrator("upgrade")), Clean, ",");

        var row = Assert.Single(rows);

        Assert.Contains(FleetGlyphs.Orchestrator, row.Text);
        Assert.Contains("upgrade", row.Text);
    }

    [Fact]
    public void An_orchestrator_row_counts_its_children()
    {
        var rows = SubRows.For(
            Listing(
                Orchestrator("upgrade"),
                Agent("backend", "story", "upgrade"),
                Agent("frontend", "form", "upgrade")),
            Clean,
            ",");

        Assert.Contains("2 agent(s)", rows[0].Text);
    }

    [Fact]
    public void The_orchestrator_status_defaults_to_working_when_blank()
    {
        var rows = SubRows.For(Listing(Orchestrator("upgrade")), Clean, ",");

        Assert.Contains(OrchestrationStatus.Working, rows[0].Text);
    }

    [Fact]
    public void A_reported_status_is_shown_verbatim()
    {
        var rows = SubRows.For(
            Listing(Orchestrator("upgrade", status: OrchestrationStatus.Done)), Clean, ",");

        Assert.Contains(OrchestrationStatus.Done, rows[0].Text);
    }

    [Fact]
    public void A_child_row_is_indented_under_its_orchestrator()
    {
        var rows = SubRows.For(
            Listing(Orchestrator("upgrade"), Agent("backend", "story", "upgrade")),
            Clean,
            ",");

        Assert.Equal(2, rows.Count);
        Assert.Contains(FleetGlyphs.Child, rows[1].Text);
        Assert.Contains("backend", rows[1].Text);
    }

    [Fact]
    public void A_hidden_orchestrator_shows_the_hidden_glyph()
    {
        var rows = SubRows.For(Listing(Orchestrator("upgrade", hidden: true)), Clean, ",");

        Assert.Contains(FleetGlyphs.Hidden, rows[0].Text);
    }
}
