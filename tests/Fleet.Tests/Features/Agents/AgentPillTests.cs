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
    public void One_pill_holds_the_branch_then_the_icon_then_the_status()
    {
        var pill = BranchStatus.Pill("dev", new BranchState(0, 0, false));

        Assert.Equal(FleetGlyphs.PillLeft, pill[0].Text);
        Assert.Equal(FleetTones.PillEdge, pill[0].Tone);
        Assert.Contains("dev", pill[1].Text);
        Assert.Equal(FleetTones.BranchName, pill[1].Tone);
        Assert.Contains(FleetGlyphs.Branch, pill[2].Text);
        Assert.Equal(FleetTones.Icon, pill[2].Tone);
        Assert.Equal(FleetGlyphs.PillRight, pill[^1].Text);
        Assert.Equal(FleetTones.PillEdge, pill[^1].Tone);
    }

    [Fact]
    public void Ahead_behind_and_dirty_each_carry_their_own_tone()
    {
        var tones = BranchStatus.Pill("dev", new BranchState(1, 4, true))
            .Select(s => s.Tone)
            .ToList();

        Assert.Equal(
            [
                FleetTones.PillEdge,
                FleetTones.BranchName,
                FleetTones.Icon,
                FleetTones.Behind,
                FleetTones.Ahead,
                FleetTones.Dirty,
                FleetTones.PillEdge,
            ],
            tones);
    }

    [Fact]
    public void The_branch_pill_leads_the_row_and_the_repository_follows_it()
    {
        var row = AgentRows.For([Agent()], _ => new BranchState(1, 4, false))[0].Text;

        var branch = row.IndexOf("dev", StringComparison.Ordinal);
        var status = row.IndexOf(FleetGlyphs.Behind, StringComparison.Ordinal);
        var repo = row.IndexOf("frontend", StringComparison.Ordinal);

        Assert.True(branch < status, "the branch opens the pill");
        Assert.True(status < repo, "the status is still inside the pill, before the repository");
    }

    [Fact]
    public void The_harness_is_not_shown_on_the_row()
    {
        var row = AgentRows.For([Agent()], _ => BranchState.Unknown)[0].Text;

        Assert.DoesNotContain(AgentHarness.Nvim, row);
    }

    [Fact]
    public void A_hidden_agent_is_marked_with_the_glyph_not_the_word()
    {
        var row = AgentRows.For([Agent() with { Hidden = true }], _ => BranchState.Unknown)[0].Text;

        Assert.Contains(FleetGlyphs.Hidden, row);
        Assert.DoesNotContain("(hidden)", row);
    }

    [Fact]
    public void The_pill_glyphs_are_the_nerd_font_codepoints()
    {
        Assert.Equal("\ue0a0", FleetGlyphs.Branch);
        Assert.Equal("\ue0b6", FleetGlyphs.PillLeft);
        Assert.Equal("\ue0b4", FleetGlyphs.PillRight);
    }

    [Fact]
    public void The_spinner_cycles_without_running_off_the_end()
    {
        Assert.Equal(FleetGlyphs.Spinner[0], FleetGlyphs.Frame(0));
        Assert.Equal(FleetGlyphs.Spinner[0], FleetGlyphs.Frame(FleetGlyphs.Spinner.Length));
        Assert.Equal(FleetGlyphs.Spinner[3], FleetGlyphs.Frame(13));
    }
}
