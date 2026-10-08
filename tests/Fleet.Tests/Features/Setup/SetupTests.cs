using Fleet.Features.Setup.RunSetup;
using Fleet.Features.Setup.RunSetup.Models;

namespace Fleet.Tests.Features.Setup;

public class SetupTests
{
    private static SetupReport Report(params string[] installed) =>
        new SetupHandler(t => installed.Contains(t))
            .Inspect("C:/Users/x/AppData/Roaming/fleet");

    [Fact]
    public void Everything_present_blocks_nothing()
    {
        var report = Report("git", "nvim", "claude", "yazi");

        Assert.False(report.Blocked);
        Assert.Empty(report.Missing);
    }

    [Fact]
    public void A_missing_git_blocks_because_fleet_cannot_work_without_it()
    {
        var report = Report("nvim", "claude", "yazi");

        Assert.True(report.Blocked);
        Assert.Contains(report.Missing, s => s.Name == "git");
    }

    [Fact]
    public void Wezterm_is_no_longer_checked()
    {
        Assert.DoesNotContain(Report().Steps, s => s.Name.Contains("wezterm", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_missing_harness_is_reported_but_does_not_block()
    {
        var report = Report("git");

        Assert.False(report.Blocked);

        Assert.Equal(
            ["nvim", "claude", "yazi"],
            report.Missing.Select(s => s.Name));
    }

    [Fact]
    public void A_missing_yazi_costs_the_pickers_rather_than_the_agents()
    {
        var step = Assert.Single(
            Report("git", "nvim", "claude").Missing, s => s.Name == "yazi");

        Assert.Contains("folder picker", step.Detail);
        Assert.False(step.Required);
    }

    [Fact]
    public void Every_missing_step_carries_the_command_that_fixes_it()
    {
        Assert.All(Report().Missing, s => Assert.NotEqual(string.Empty, s.Fix));
    }

    [Fact]
    public void Fleets_nvim_config_on_a_new_enough_nvim_is_ok()
    {
        var step = SetupHandler.Nvim(new NvimSetup(true, true, "C:/x/fleet-nvim", new Version(0, 10, 2)));

        Assert.True(step.Ok);
        Assert.Contains("C:/x/fleet-nvim", step.Detail);
    }

    [Fact]
    public void An_nvim_older_than_0_9_is_told_to_upgrade_or_switch_to_user()
    {
        var step = SetupHandler.Nvim(new NvimSetup(true, true, "C:/x/fleet-nvim", new Version(0, 8, 3)));

        Assert.False(step.Ok);
        Assert.False(step.Required);
        Assert.Contains("user", step.Fix);
    }

    [Fact]
    public void The_user_setting_skips_the_version_check()
    {
        Assert.True(SetupHandler.Nvim(new NvimSetup(false, false, "C:/x/fleet-nvim", new Version(0, 8, 0))).Ok);
    }

    [Fact]
    public void The_nvim_step_shows_up_in_the_report_when_given()
    {
        var report = new SetupHandler(_ => true).Inspect(
            "c", new NvimSetup(true, true, "C:/x/fleet-nvim", new Version(0, 11, 0)));

        Assert.Contains(report.Steps, s => s.Name == "nvim config" && s.Ok);
    }
}
