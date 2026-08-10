using Fleet.Features.Agents.ListAgents;
using Fleet.Ports.Agents.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;
using Fleet.Ui;
using Fleet.Ui.Constants;

namespace Fleet.Tests.Features.Agents;

public class AgentPillTests
{
    private static AgentRecord Agent(string branch = "dev", string repo = "frontend") =>
        new($"C:/repos/techweb/{repo}/{branch}", repo, branch, AgentHarness.Nvim,
            "origin/develop", true);

    [Fact]
    public void A_branch_level_with_its_upstream_shows_no_status()
    {
        Assert.Empty(BranchStatus.Of(new BranchState(0, 0, false)));
    }

    [Fact]
    public void Behind_comes_before_ahead_as_lazygit_shows_it()
    {
        Assert.Equal(
            $"{FleetGlyphs.Behind}4{FleetGlyphs.Ahead}1",
            BranchStatus.Of(new BranchState(1, 4, false)));
    }

    [Fact]
    public void Only_the_side_that_is_nonzero_is_shown()
    {
        Assert.Equal($"{FleetGlyphs.Ahead}2", BranchStatus.Of(new BranchState(2, 0, false)));
        Assert.Equal($"{FleetGlyphs.Behind}3", BranchStatus.Of(new BranchState(0, 3, false)));
    }

    [Fact]
    public void Uncommitted_work_is_marked()
    {
        Assert.Equal(FleetGlyphs.Dirty, BranchStatus.Of(new BranchState(0, 0, true)));
    }

    [Fact]
    public void The_branch_leads_the_row_then_the_repository_then_the_status()
    {
        var row = AgentRows.For([Agent()], _ => new BranchState(1, 4, false))[0];

        var branch = row.IndexOf("dev", StringComparison.Ordinal);
        var repo = row.IndexOf("frontend", StringComparison.Ordinal);
        var status = row.IndexOf(FleetGlyphs.Behind, StringComparison.Ordinal);

        Assert.True(branch < repo, "the branch comes first");
        Assert.True(repo < status, "the status comes last");
    }

    [Fact]
    public void The_harness_is_not_shown_on_the_row()
    {
        var row = AgentRows.For([Agent()], _ => BranchState.Unknown)[0];

        Assert.DoesNotContain(AgentHarness.Nvim, row);
    }

    [Fact]
    public void A_hidden_agent_still_says_so()
    {
        var row = AgentRows.For([Agent() with { Hidden = true }], _ => BranchState.Unknown)[0];

        Assert.Contains("(hidden)", row);
    }

    [Fact]
    public void The_spinner_cycles_without_running_off_the_end()
    {
        Assert.Equal(FleetGlyphs.Spinner[0], FleetGlyphs.Frame(0));
        Assert.Equal(FleetGlyphs.Spinner[0], FleetGlyphs.Frame(FleetGlyphs.Spinner.Length));
        Assert.Equal(FleetGlyphs.Spinner[3], FleetGlyphs.Frame(13));
    }
}
