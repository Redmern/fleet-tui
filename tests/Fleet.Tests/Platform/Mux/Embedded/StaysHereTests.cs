using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Protocol;

namespace Fleet.Tests.Platform.Mux.Embedded;

public class StaysHereTests
{
    [Theory]
    [InlineData("remotes", true)]
    [InlineData("Remotes", true)]
    [InlineData(" remotes ", true)]
    [InlineData("SWITCH-PROJECT", true)]
    [InlineData("save-session", true)]
    [InlineData("keybinds", true)]
    [InlineData("theme", true)]
    [InlineData("show-menu-keys", true)]
    [InlineData("button-hints", true)]
    [InlineData("this-machine-settings", true)]
    [InlineData("viewlogs", false)]
    [InlineData("update", false)]
    [InlineData("version", false)]
    [InlineData("whats-new", false)]
    [InlineData("list-agents", false)]
    [InlineData(" Quit ", false)]
    [InlineData(null, false)]
    public void A_menu_chord_stays_here_when_its_action_reads_as_an_owned_screen(string? arg, bool stays) =>
        Assert.Equal(stays, FleetDaemon.StaysHere(new CommandMessage { Name = "menu", Arg = arg }));

    [Theory]
    [InlineData("head", true)]
    [InlineData("switch-project", true)]
    [InlineData("split", false)]
    [InlineData("copy-mode", false)]
    public void A_command_that_belongs_to_the_window_stays_here(string name, bool stays) =>
        Assert.Equal(stays, FleetDaemon.StaysHere(new CommandMessage { Name = name }));
}
