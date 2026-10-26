using System.ComponentModel;
using Fleet.Platform.Uninstall;
using Fleet.Ports.Uninstall.Enums;
using Fleet.Ports.Uninstall.Models;

namespace Fleet.Tests.Platform.Uninstall;

public class PackageRemoverTests
{
    private static InstalledDependency Dep(PackageManager manager, string package, bool sudo = false) =>
        new(DependencyTool.Nvim, manager, package, sudo, "2026-10-23T05:00:00Z");

    [Theory]
    [InlineData(PackageManager.Brew, "neovim", false, new[] { "brew", "uninstall", "neovim" })]
    [InlineData(PackageManager.Pacman, "neovim", true, new[] { "sudo", "pacman", "-R", "--noconfirm", "neovim" })]
    [InlineData(PackageManager.Pacman, "neovim", false, new[] { "pacman", "-R", "--noconfirm", "neovim" })]
    [InlineData(PackageManager.Dnf, "neovim", true, new[] { "sudo", "dnf", "remove", "-y", "neovim" })]
    [InlineData(PackageManager.Dnf, "neovim", false, new[] { "dnf", "remove", "-y", "neovim" })]
    [InlineData(PackageManager.Apt, "neovim", true, new[] { "sudo", "apt-get", "remove", "-y", "neovim" })]
    [InlineData(PackageManager.Apt, "neovim", false, new[] { "apt-get", "remove", "-y", "neovim" })]
    [InlineData(PackageManager.Winget, "Neovim.Neovim", false, new[] { "winget", "uninstall", "--id", "Neovim.Neovim", "-e" })]
    [InlineData(PackageManager.Brew, "python@3.12", true, new[] { "sudo", "brew", "uninstall", "python@3.12" })]
    public void Each_manager_gets_its_exact_non_recursive_command(PackageManager manager, string package, bool sudo, string[] expected) =>
        Assert.Equal(expected, PackageRemover.CommandFor(Dep(manager, package, sudo)));

    [Theory]
    [InlineData("neovim; rm -rf ~")]
    [InlineData("-Rs")]
    [InlineData("--purge")]
    [InlineData("$(id)")]
    [InlineData("neo vim")]
    [InlineData("neovim\n")]
    [InlineData("a`b`")]
    [InlineData("../x")]
    [InlineData("")]
    public void A_hostile_package_name_gets_no_command(string package) =>
        Assert.Null(PackageRemover.CommandFor(Dep(PackageManager.Pacman, package, sudo: true)));

    [Fact]
    public void A_hostile_package_name_is_refused_without_running_anything()
    {
        var ran = 0;

        var result = new PackageRemover(true, _ => ++ran).Remove(Dep(PackageManager.Apt, "x; reboot"));

        Assert.False(result.Succeeded);
        Assert.Equal(0, ran);
        Assert.Contains("x; reboot", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void It_runs_the_command_and_succeeds_on_exit_0()
    {
        string[]? ran = null;

        var result = new PackageRemover(true, argv =>
        {
            ran = argv;
            return 0;
        }).Remove(Dep(PackageManager.Pacman, "neovim", sudo: true));

        Assert.True(result.Succeeded);
        Assert.Equal(["sudo", "pacman", "-R", "--noconfirm", "neovim"], ran!);
    }

    [Fact]
    public void A_non_zero_exit_is_a_failure_naming_the_code()
    {
        var result = new PackageRemover(true, _ => 3).Remove(Dep(PackageManager.Brew, "neovim"));

        Assert.False(result.Succeeded);
        Assert.Contains("3", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_package_manager_is_a_failure_not_a_crash()
    {
        var result = new PackageRemover(true, _ => throw new Win32Exception("No such file or directory"))
            .Remove(Dep(PackageManager.Dnf, "neovim"));

        Assert.False(result.Succeeded);
        Assert.Contains("No such file", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Sudo_without_a_terminal_fails_without_running()
    {
        var ran = 0;

        var result = new PackageRemover(false, _ => ++ran).Remove(Dep(PackageManager.Pacman, "neovim", sudo: true));

        Assert.False(result.Succeeded);
        Assert.Equal(0, ran);
        Assert.Contains("terminal", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void No_sudo_runs_without_a_terminal()
    {
        var result = new PackageRemover(false, _ => 0).Remove(Dep(PackageManager.Winget, "sxyazi.yazi"));

        Assert.True(result.Succeeded);
    }
}
