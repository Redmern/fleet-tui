using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Enums;
using Fleet.Ports.Mux.Exceptions;
using Fleet.Ports.Mux.Models;

namespace Fleet.Tests.Platform.Mux;

public class FakeMuxDriverTests
{
    [Fact]
    public async Task Spawn_creates_a_pane_in_a_new_window()
    {
        var mux = new FakeMuxDriver();

        var id = await mux.SpawnAsync(new SpawnOptions
        {
            NewWindow = true,
            SessionName = "backend",
            Cwd = "/repos/backend",
            Args = ["claude"],
        });

        var pane = Assert.Single(await mux.ListPanesAsync());
        Assert.Equal(id, pane.Id);
        Assert.Equal("backend", pane.SessionName);
        Assert.Equal("/repos/backend", pane.Cwd);
        Assert.Equal(["claude"], mux.ArgsFor(id));
    }

    [Fact]
    public async Task Split_puts_the_new_pane_in_the_same_window()
    {
        var mux = new FakeMuxDriver();
        var left = await mux.SpawnAsync(new SpawnOptions { NewWindow = true, Args = ["claude"] });

        var right = await mux.SplitAsync(new SplitOptions(left, SplitDirection.Right)
        {
            Percent = 50,
            Args = ["fleet", "dash"],
        });

        var panes = await mux.ListPanesAsync();
        Assert.Equal(2, panes.Count);
        Assert.Equal(
            panes.Single(p => p.Id == left).WindowId,
            panes.Single(p => p.Id == right).WindowId);
    }

    [Fact]
    public async Task Split_inherits_the_source_pane_cwd_when_none_is_given()
    {
        var mux = new FakeMuxDriver();
        var left = await mux.SpawnAsync(new SpawnOptions { NewWindow = true, Cwd = "/repos/x" });

        var right = await mux.SplitAsync(new SplitOptions(left, SplitDirection.Right));

        Assert.Equal("/repos/x", (await mux.ListPanesAsync()).Single(p => p.Id == right).Cwd);
    }

    [Fact]
    public async Task Two_new_windows_get_different_window_ids()
    {
        var mux = new FakeMuxDriver();
        await mux.SpawnAsync(new SpawnOptions { NewWindow = true });
        await mux.SpawnAsync(new SpawnOptions { NewWindow = true });

        Assert.Equal(2, (await mux.ListPanesAsync()).Select(p => p.WindowId).Distinct().Count());
    }

    [Fact]
    public async Task Split_of_an_unknown_pane_throws()
    {
        var mux = new FakeMuxDriver();

        await Assert.ThrowsAsync<MuxUnavailableException>(
            () => mux.SplitAsync(new SplitOptions(new PaneId("nope"), SplitDirection.Right)));
    }

    [Fact]
    public async Task Focus_marks_exactly_one_pane_active()
    {
        var mux = new FakeMuxDriver();
        var a = await mux.SpawnAsync(new SpawnOptions { NewWindow = true });
        var b = await mux.SplitAsync(new SplitOptions(a, SplitDirection.Right));

        await mux.FocusPaneAsync(a);

        var panes = await mux.ListPanesAsync();
        Assert.True(panes.Single(p => p.Id == a).IsActive);
        Assert.False(panes.Single(p => p.Id == b).IsActive);
    }

    [Fact]
    public async Task SetTitle_records_the_title()
    {
        var mux = new FakeMuxDriver();
        var id = await mux.SpawnAsync(new SpawnOptions { NewWindow = true });

        await mux.SetTitleAsync(id, "backend");

        Assert.Equal("backend", mux.TitleOf(id));
    }

    [Fact]
    public async Task IsAvailable_can_be_turned_off_to_simulate_a_missing_mux()
        => Assert.False(await new FakeMuxDriver { Available = false }.IsAvailableAsync());

    [Fact]
    public async Task Every_verb_throws_when_unavailable()
    {
        var mux = new FakeMuxDriver { Available = false };

        await Assert.ThrowsAsync<MuxUnavailableException>(() => mux.ListPanesAsync());
        await Assert.ThrowsAsync<MuxUnavailableException>(() => mux.SpawnAsync(new SpawnOptions()));
        await Assert.ThrowsAsync<MuxUnavailableException>(
            () => mux.SetTitleAsync(new PaneId("p1"), "x"));
    }
}
