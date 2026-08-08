using Fleet.Platform.Mux;

namespace Fleet.Tests.Platform.Mux;

public class DriverSelectorTests
{
    private static readonly HashSet<string> BothInstalled = ["wezterm", "tmux"];

    [Fact]
    public void Explicit_override_wins_over_everything()
        => Assert.Equal("tmux", DriverSelector.Choose(new MuxEnvironment
        {
            Override = "tmux",
            InsideWezTerm = true,
            GuiReachable = true,
            Installed = BothInstalled,
        }));

    [Fact]
    public void An_override_is_normalized()
        => Assert.Equal("wezterm", DriverSelector.Choose(new MuxEnvironment
        {
            Override = "  WezTerm  ",
        }));

    [Fact]
    public void Inside_tmux_adopts_tmux_even_though_wezterm_is_the_base()
        => Assert.Equal("tmux", DriverSelector.Choose(new MuxEnvironment
        {
            InsideTmux = true,
            GuiReachable = true,
            Installed = BothInstalled,
        }));

    [Fact]
    public void Inside_wezterm_adopts_wezterm()
        => Assert.Equal("wezterm", DriverSelector.Choose(new MuxEnvironment
        {
            InsideWezTerm = true,
            Installed = BothInstalled,
        }));

    [Fact]
    public void Launching_with_a_gui_prefers_wezterm()
        => Assert.Equal("wezterm", DriverSelector.Choose(new MuxEnvironment
        {
            GuiReachable = true,
            Installed = BothInstalled,
        }));

    [Fact]
    public void Launching_without_a_gui_falls_back_to_tmux()
        => Assert.Equal("tmux", DriverSelector.Choose(new MuxEnvironment
        {
            GuiReachable = false,
            Installed = BothInstalled,
        }));

    [Fact]
    public void Launching_with_a_gui_but_no_wezterm_falls_back_to_tmux()
        => Assert.Equal("tmux", DriverSelector.Choose(new MuxEnvironment
        {
            GuiReachable = true,
            Installed = new HashSet<string> { "tmux" },
        }));

    [Fact]
    public void Launching_with_nothing_installed_falls_through_to_embedded()
        => Assert.Equal("embedded", DriverSelector.Choose(new MuxEnvironment
        {
            GuiReachable = true,
            Installed = new HashSet<string>(),
        }));

    [Fact]
    public void OnPath_finds_a_real_executable_and_rejects_a_made_up_one()
    {
        var real = OperatingSystem.IsWindows() ? "cmd" : "sh";

        Assert.True(MuxEnvironment.OnPath(real));
        Assert.False(MuxEnvironment.OnPath("fleet-definitely-not-a-real-binary"));
    }
}
