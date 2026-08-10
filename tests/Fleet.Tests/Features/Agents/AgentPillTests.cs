using Fleet.Features.Agents.ListAgents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Git.Models;
using Fleet.Shared.Constants;
using Fleet.Ui.Constants;

namespace Fleet.Tests.Features.Agents;

public class AgentPillTests
{
    private static AgentRecord Agent(string branch = "dev") =>
        new($"C:/repos/techweb/backend/{branch}", "backend", branch, AgentHarness.Nvim,
            "origin/develop", true);

    [Fact]
    public void A_branch_with_no_commits_ahead_is_just_the_branch()
    {
        Assert.Equal($"{FleetGlyphs.Branch} dev", AgentRows.Pill("dev", new BranchState(0, false)));
    }

    [Fact]
    public void Commits_ahead_of_the_base_are_counted_in_the_pill()
    {
        Assert.Equal($"{FleetGlyphs.Branch} dev +3", AgentRows.Pill("dev", new BranchState(3, false)));
    }

    [Fact]
    public void Uncommitted_work_is_marked()
    {
        Assert.Equal($"{FleetGlyphs.Branch} dev*", AgentRows.Pill("dev", new BranchState(0, true)));
        Assert.Equal($"{FleetGlyphs.Branch} dev +2*", AgentRows.Pill("dev", new BranchState(2, true)));
    }

    [Fact]
    public void Every_row_carries_the_branch_glyph()
    {
        var rows = AgentRows.For([Agent()], _ => new BranchState(1, false));

        Assert.Contains(FleetGlyphs.Branch, rows[0]);
        Assert.Contains("+1", rows[0]);
    }

    [Fact]
    public void The_spinner_cycles_without_running_off_the_end()
    {
        Assert.Equal(FleetGlyphs.Spinner[0], FleetGlyphs.Frame(0));
        Assert.Equal(FleetGlyphs.Spinner[0], FleetGlyphs.Frame(FleetGlyphs.Spinner.Length));
        Assert.Equal(FleetGlyphs.Spinner[3], FleetGlyphs.Frame(13));
    }
}
