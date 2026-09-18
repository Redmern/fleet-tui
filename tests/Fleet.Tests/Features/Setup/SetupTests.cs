using Fleet.Features.Setup.RunSetup;
using Fleet.Features.Setup.RunSetup.Enums;
using Fleet.Features.Setup.RunSetup.Models;

namespace Fleet.Tests.Features.Setup;

public class SetupTests
{
    private static readonly ConfigWiring Wired =
        new(WiringState.Added, "C:/Users/x/.wezterm.lua");

    private static SetupReport Report(params string[] installed) =>
        new SetupHandler(t => installed.Contains(t))
            .Inspect("C:/Users/x/.wezterm/fleet.lua", Wired, "C:/Users/x/AppData/Roaming/fleet");

    [Fact]
    public void Everything_present_blocks_nothing()
    {
        var report = Report("wezterm", "git", "nvim", "claude", "yazi");

        Assert.False(report.Blocked);
        Assert.Empty(report.Missing);
    }

    [Fact]
    public void A_missing_multiplexer_blocks_because_fleet_cannot_work_without_it()
    {
        var report = Report("git", "nvim", "claude", "yazi");

        Assert.True(report.Blocked);
        Assert.Contains(report.Missing, s => s.Name == "wezterm");
    }

    [Fact]
    public void A_missing_harness_is_reported_but_does_not_block()
    {
        var report = Report("wezterm", "git");

        Assert.False(report.Blocked);

        Assert.Equal(
            ["nvim", "claude", "yazi"],
            report.Missing.Select(s => s.Name));
    }

    [Fact]
    public void A_missing_yazi_costs_the_pickers_rather_than_the_agents()
    {
        var step = Assert.Single(
            Report("wezterm", "git", "nvim", "claude").Missing, s => s.Name == "yazi");

        Assert.Contains("folder picker", step.Detail);
        Assert.False(step.Required);
    }

    [Fact]
    public void Every_missing_step_carries_the_command_that_fixes_it()
    {
        Assert.All(Report().Missing, s => Assert.NotEqual(string.Empty, s.Fix));
    }

    [Fact]
    public void An_unwritten_dedicated_config_is_reported_and_told_to_create_it()
    {
        var report = new SetupHandler(_ => true).Inspect(
            "fleet.lua",
            new ConfigWiring(WiringState.Missing, "~/.fleet/wezterm/fleet-instance.lua"),
            "config");

        var step = Assert.Single(report.Missing);

        Assert.Equal("fleet wezterm", step.Name);
        Assert.False(report.Blocked);
        Assert.Contains("fleet-instance.lua", step.Fix);
    }

    [Fact]
    public void A_config_that_could_not_be_written_says_why()
    {
        var report = new SetupHandler(_ => true).Inspect(
            "fleet.lua",
            new ConfigWiring(
                WiringState.Failed, "~/.fleet/wezterm/fleet-instance.lua", "access denied"),
            "config");

        var step = Assert.Single(report.Missing);

        Assert.Equal("access denied", step.Detail);
    }

    [Fact]
    public void An_already_wired_config_is_left_alone_and_still_counts_as_done()
    {
        var report = new SetupHandler(_ => true).Inspect(
            "fleet.lua",
            new ConfigWiring(WiringState.Already, "~/.wezterm.lua"),
            "config");

        Assert.Empty(report.Missing);
    }
}
