using Fleet.Cli.Composition;
using Fleet.Ports.Mux.Models;

namespace Fleet.Tests.Cli;

public class SettingsEditorWiringTests
{
    private static readonly PaneId Nvim = new("nvim-1");

    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(1);

    private static Pane Listed(PaneId id) => new(id, "w1", "t1", "techweb", "fleet settings", "/repos/techweb", false);

    [Fact]
    public async Task The_exit_fires_once_the_nvim_pane_is_gone()
    {
        var polls = 0;
        var exits = 0;

        await SettingsEditorWiring.WaitForExitAsync(
            () => Task.FromResult<IReadOnlyList<Pane>>(++polls < 3 ? [Listed(Nvim)] : []),
            Nvim,
            () => exits++,
            Tick,
            TimeSpan.FromMinutes(1));

        Assert.Equal(3, polls);
        Assert.Equal(1, exits);
    }

    [Fact]
    public async Task A_multiplexer_that_throws_still_fires_the_exit()
    {
        var exits = 0;

        await SettingsEditorWiring.WaitForExitAsync(
            () => throw new IOException("fleetd went away"),
            Nvim,
            () => exits++,
            Tick,
            TimeSpan.FromMinutes(1));

        Assert.Equal(1, exits);
    }

    [Fact]
    public async Task A_pane_that_never_closes_fires_the_exit_at_the_deadline()
    {
        var exits = 0;

        await SettingsEditorWiring.WaitForExitAsync(
            () => Task.FromResult<IReadOnlyList<Pane>>([Listed(Nvim)]),
            Nvim,
            () => exits++,
            Tick,
            TimeSpan.FromMilliseconds(30));

        Assert.Equal(1, exits);
    }
}
