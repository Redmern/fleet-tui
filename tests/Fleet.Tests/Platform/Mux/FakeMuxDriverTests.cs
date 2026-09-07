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
    public async Task Split_puts_the_new_pane_in_the_same_tab_like_wezterm_does()
    {
        var mux = new FakeMuxDriver();
        var left = await mux.SpawnAsync(new SpawnOptions { NewWindow = true, Args = ["claude"] });
        var right = await mux.SplitAsync(new SplitOptions(left, SplitDirection.Right));

        var panes = await mux.ListPanesAsync();

        Assert.Equal(panes.Single(p => p.Id == left).TabId, panes.Single(p => p.Id == right).TabId);
    }

    [Fact]
    public async Task A_title_belongs_to_the_tab_so_every_pane_in_it_reports_the_last_one_set()
    {
        var mux = new FakeMuxDriver();
        var left = await mux.SpawnAsync(new SpawnOptions { NewWindow = true, Args = ["claude"] });
        var right = await mux.SplitAsync(new SplitOptions(left, SplitDirection.Right));

        await mux.SetTitleAsync(left, "sub");
        await mux.SetTitleAsync(right, "sub files");

        Assert.Equal("sub files", mux.TitleOf(left));
        Assert.Equal("sub files", mux.TitleOf(right));
    }

    [Fact]
    public async Task A_pane_moved_to_a_new_tab_leaves_the_title_behind_and_falls_back_to_its_own()
    {
        var mux = new FakeMuxDriver();
        var left = await mux.SpawnAsync(new SpawnOptions { NewWindow = true, Args = ["claude"] });
        var right = await mux.SplitAsync(new SplitOptions(left, SplitDirection.Right));
        mux.SetPaneTitle(right, "nvim.exe");
        await mux.SetTitleAsync(left, "sub");

        await mux.MovePaneAsync(right, new MovePaneOptions { NewWindow = true });

        var panes = await mux.ListPanesAsync();
        Assert.NotEqual(panes.Single(p => p.Id == left).TabId, panes.Single(p => p.Id == right).TabId);
        Assert.Equal("sub", mux.TitleOf(left));
        Assert.Equal("nvim.exe", mux.TitleOf(right));
    }

    [Fact]
    public async Task A_titled_command_line_names_its_own_pane()
    {
        var mux = new FakeMuxDriver();
        await mux.SpawnAsync(new SpawnOptions
        {
            NewWindow = true,
            Args = Fleet.Shared.Constants.AgentHarness.BrowseCommandFor("sub files"),
        });

        var pane = Assert.Single(await mux.ListPanesAsync());
        Assert.Equal("sub files", pane.PaneTitle);
        Assert.Equal("sub files", pane.Title);
    }

    [Fact]
    public async Task Split_can_move_an_existing_pane_into_the_source_tab_instead_of_spawning()
    {
        var mux = new FakeMuxDriver();
        var host = await mux.SpawnAsync(new SpawnOptions { NewWindow = true, Args = ["claude"] });
        var stray = await mux.SpawnAsync(new SpawnOptions { NewWindow = true, Args = ["fleet", "dash"] });

        var moved = await mux.SplitAsync(new SplitOptions(host, SplitDirection.Right) { MovePane = stray });

        var panes = await mux.ListPanesAsync();
        Assert.Equal(stray, moved);
        Assert.Equal(2, panes.Count);
        Assert.Equal(panes.Single(p => p.Id == host).TabId, panes.Single(p => p.Id == stray).TabId);
        Assert.Equal(panes.Single(p => p.Id == host).WindowId, panes.Single(p => p.Id == stray).WindowId);
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
