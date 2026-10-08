using Fleet.Features.Setup.RunSetup;

namespace Fleet.Tests.Features.Setup;

public class TroubleTests
{
    [Fact]
    public void Embedded_chosen_in_a_build_that_carries_the_emulator_is_fine()
    {
        Assert.Null(MuxTrouble.With("embedded", embeddedReady: true));
    }

    [Fact]
    public void Embedded_chosen_in_a_build_without_the_emulator_says_so()
    {
        var trouble = MuxTrouble.With("embedded", embeddedReady: false);

        Assert.NotNull(trouble);
        Assert.Contains("libghostty-vt", trouble);
        Assert.DoesNotContain("wezterm", trouble);
    }

    [Theory]
    [InlineData("tmux")]
    [InlineData("wezterm")]
    public void Any_other_driver_is_the_real_gap(string chosen)
    {
        var trouble = MuxTrouble.With(chosen);

        Assert.NotNull(trouble);
        Assert.Contains(chosen, trouble);
        Assert.Contains("not implemented", trouble);
        Assert.Contains("FLEET_MUX", trouble);
    }

    [Fact]
    public void A_missing_harness_says_which_one_and_what_to_do()
    {
        var trouble = HarnessTrouble.Missing("nvim");

        Assert.StartsWith("nvim is not on PATH", trouble);
        Assert.Contains("change what this agent opens", trouble);
    }
}
