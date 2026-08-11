using Fleet.Features.Setup.RunSetup;

namespace Fleet.Tests.Features.Setup;

public class TroubleTests
{
    [Fact]
    public void Wezterm_chosen_means_there_is_nothing_to_report()
    {
        Assert.Null(MuxTrouble.With("wezterm", wezTermOnPath: true));
        Assert.Null(MuxTrouble.With("wezterm", wezTermOnPath: false));
    }

    [Fact]
    public void A_machine_without_wezterm_is_told_to_install_it_not_that_a_driver_is_missing()
    {
        var trouble = MuxTrouble.With("embedded", wezTermOnPath: false);

        Assert.NotNull(trouble);
        Assert.Contains("wezterm is not installed", trouble);
        Assert.Contains("fleet setup", trouble);
        Assert.DoesNotContain("embedded", trouble);
    }

    [Fact]
    public void Inside_tmux_with_wezterm_present_the_driver_is_the_real_gap()
    {
        var trouble = MuxTrouble.With("tmux", wezTermOnPath: true);

        Assert.NotNull(trouble);
        Assert.Contains("tmux", trouble);
        Assert.Contains("not implemented", trouble);
    }

    [Fact]
    public void A_missing_harness_says_which_one_and_what_to_do()
    {
        var trouble = HarnessTrouble.Missing("nvim");

        Assert.StartsWith("nvim is not on PATH", trouble);
        Assert.Contains("change what this agent opens", trouble);
    }
}
