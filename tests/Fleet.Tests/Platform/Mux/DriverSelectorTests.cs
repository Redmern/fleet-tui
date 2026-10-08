using Fleet.Platform.Mux;
using Fleet.Platform.Mux.Models;

namespace Fleet.Tests.Platform.Mux;

public class DriverSelectorTests
{
    private static readonly HashSet<string> TmuxInstalled = ["tmux"];

    [Fact]
    public void Explicit_override_wins_over_everything()
        => Assert.Equal("tmux", DriverSelector.Choose(new MuxEnvironment
        {
            Override = "tmux",
            Installed = TmuxInstalled,
            EmbeddedReady = true,
        }));

    [Fact]
    public void An_override_is_normalized()
        => Assert.Equal("tmux", DriverSelector.Choose(new MuxEnvironment
        {
            Override = "  TMUX  ",
        }));

    [Fact]
    public void Inside_tmux_adopts_tmux()
        => Assert.Equal("tmux", DriverSelector.Choose(new MuxEnvironment
        {
            InsideTmux = true,
            Installed = TmuxInstalled,
        }));

    [Fact]
    public void Without_the_embedded_multiplexer_an_installed_tmux_is_the_fallback()
        => Assert.Equal("tmux", DriverSelector.Choose(new MuxEnvironment
        {
            Installed = TmuxInstalled,
        }));

    [Fact]
    public void Launching_with_nothing_installed_falls_through_to_embedded()
        => Assert.Equal("embedded", DriverSelector.Choose(new MuxEnvironment
        {
            Installed = new HashSet<string>(),
        }));

    [Fact]
    public void A_plain_terminal_uses_the_embedded_multiplexer_when_this_build_has_it()
        => Assert.Equal("embedded", DriverSelector.Choose(new MuxEnvironment
        {
            Installed = TmuxInstalled,
            EmbeddedReady = true,
        }));

    [Fact]
    public void Inside_tmux_its_driver_still_wins_over_embedded()
        => Assert.Equal("tmux", DriverSelector.Choose(new MuxEnvironment
        {
            InsideTmux = true,
            Installed = TmuxInstalled,
            EmbeddedReady = true,
        }));

    [Fact]
    public void A_wezterm_installation_no_longer_takes_fleet_away_from_embedded()
        => Assert.Equal("embedded", DriverSelector.Choose(new MuxEnvironment
        {
            Installed = new HashSet<string> { "wezterm" },
            EmbeddedReady = true,
        }));

    [Fact]
    public void OnPath_finds_a_real_executable_and_rejects_a_made_up_one()
    {
        var real = OperatingSystem.IsWindows() ? "cmd" : "sh";

        Assert.True(MuxEnvironment.OnPath(real));
        Assert.False(MuxEnvironment.OnPath("fleet-definitely-not-a-real-binary"));
    }
}
