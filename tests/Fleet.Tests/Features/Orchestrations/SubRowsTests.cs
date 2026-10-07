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
    public void A_gap_separates_each_sub_group_but_never_trails_the_last()
    {
        var listing = Listing(
            Orchestrator("alpha"),
            Agent("backend", "story", "alpha"),
            Agent("frontend", "form", "alpha"),
            Orchestrator("zeta"));

        Assert.Equal([2], SubRows.GapsAfter(listing));
        Assert.Empty(SubRows.GapsAfter(Listing(Orchestrator("solo"))));
    }

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
    public void An_orchestrator_row_does_not_count_its_children()
    {
        var rows = SubRows.For(
            Listing(
                Orchestrator("upgrade"),
                Agent("backend", "story", "upgrade"),
                Agent("frontend", "form", "upgrade")),
            Clean,
            ",");

        Assert.DoesNotContain("agent(s)", rows[0].Text);
    }

    [Fact]
    public void The_orchestrator_status_defaults_to_working_when_blank()
    {
        var rows = SubRows.For(Listing(Orchestrator("upgrade")), Clean, ",");

        Assert.Contains(rows[0].Trailing!, s => s.Text.StartsWith(FleetGlyphs.Working, StringComparison.Ordinal) && s.Tone == FleetTones.Warn);
        Assert.DoesNotContain(OrchestrationStatus.Working, rows[0].Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("done", FleetGlyphs.Done, FleetTones.Good)]
    [InlineData("failed", FleetGlyphs.Failed, FleetTones.Bad)]
    [InlineData("working", FleetGlyphs.Working, FleetTones.Warn)]
    [InlineData("waiting", FleetGlyphs.Waiting, FleetTones.Bad)]
    [InlineData("stalled", FleetGlyphs.Stalled, FleetTones.Bad)]
    [InlineData("idle", FleetGlyphs.Idle, FleetTones.Good)]
    [InlineData("something else", FleetGlyphs.Working, FleetTones.Warn)]
    public void The_orchestrator_status_is_an_icon_coloured_by_status(string status, string glyph, string tone)
    {
        var rows = SubRows.For(Listing(Orchestrator("upgrade", status: status)), Clean, ",");

        var icon = rows[0].Trailing![0];
        Assert.Equal($"{glyph}  ", icon.Text);
        Assert.Equal(tone, icon.Tone);
    }

    [Fact]
    public void Statuses_name_each_orchestrator_row_and_leave_child_rows_blank()
    {
        var listing = Listing(
            Orchestrator("upgrade", status: "waiting"),
            Agent("backend", "story", "upgrade"),
            Orchestrator("zeta"));

        Assert.Equal(["waiting", string.Empty, OrchestrationStatus.Working], SubRows.Statuses(listing));
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
    public void A_visible_orchestrator_keeps_its_status_in_the_column_of_a_hidden_one()
    {
        var rows = SubRows.For(
            Listing(Orchestrator("upgrade", "working", hidden: true), Orchestrator("research", "working", hidden: false)),
            Clean,
            ",");

        var hidden = rows.Single(r => r.Text.Contains("upgrade", StringComparison.Ordinal));
        var visible = rows.Single(r => r.Text.Contains("research", StringComparison.Ordinal));

        Assert.Contains(FleetGlyphs.Hidden, hidden.Text);
        Assert.DoesNotContain(FleetGlyphs.Hidden, visible.Text);
        Assert.Equal(hidden.Trailing![0], visible.Trailing![0]);
        Assert.Equal(Width(hidden.Trailing!), Width(visible.Trailing!));
    }

    [Fact]
    public void Hidden_and_visible_children_reserve_the_same_trailing_width()
    {
        var hidden = Agent("backend", "story", "upgrade") with { Hidden = true };
        var rows = SubRows.For(Listing(Orchestrator("upgrade"), hidden, Agent("backend", "other", "upgrade")), Clean, ",");

        Assert.Equal(Width(rows[1].Trailing!), Width(rows[2].Trailing!));
    }

    private static int Width(IEnumerable<Fleet.Ui.Models.FleetSpan> spans) => spans.Sum(s => s.Text.EnumerateRunes().Count());

    [Fact]
    public void A_hidden_orchestrator_shows_the_hidden_glyph()
    {
        var rows = SubRows.For(Listing(Orchestrator("upgrade", hidden: true)), Clean, ",");

        Assert.Contains(FleetGlyphs.Hidden, rows[0].Text);
    }
}
