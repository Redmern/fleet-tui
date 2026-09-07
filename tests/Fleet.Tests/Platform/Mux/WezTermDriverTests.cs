using Fleet.Platform.Mux.WezTerm;
using Fleet.Ports.Mux.Enums;
using Fleet.Ports.Mux.Models;

namespace Fleet.Tests.Platform.Mux;

public class WezTermDriverTests
{
    private const string RealOutput = """
        [
          {
            "window_id": 0,
            "tab_id": 58,
            "pane_id": 69,
            "workspace": "default",
            "size": { "rows": 46, "cols": 210, "pixel_width": 1890, "pixel_height": 1104, "dpi": 96 },
            "title": "~/repos/fleet",
            "cwd": "file:///C:/repos/fleet/",
            "cursor_x": 0,
            "cursor_y": 9,
            "cursor_shape": "BlinkingBlock",
            "cursor_visibility": "Visible",
            "left_col": 0,
            "top_row": 0,
            "tab_title": "",
            "window_title": "a window title",
            "is_active": true,
            "is_zoomed": false
          },
          {
            "window_id": 0,
            "tab_id": 58,
            "pane_id": 70,
            "workspace": "backend",
            "title": "claude",
            "cwd": "file:///C:/repos/backend/",
            "tab_title": "backend",
            "is_active": false,
            "is_zoomed": false
          }
        ]
        """;

    [Fact]
    public void ParsePanes_reads_every_field_fleet_depends_on()
    {
        var panes = WezTermDriver.ParsePanes(RealOutput);

        Assert.Equal(2, panes.Count);

        var first = panes[0];
        Assert.Equal("69", first.Id.Value);
        Assert.Equal("default", first.SessionName);
        Assert.True(first.IsActive);

        var second = panes[1];
        Assert.Equal("70", second.Id.Value);
        Assert.Equal("backend", second.SessionName);
        Assert.False(second.IsActive);
    }

    [Fact]
    public void ParsePanes_keeps_the_panes_own_title_next_to_the_shared_tab_title()
    {
        var panes = WezTermDriver.ParsePanes(RealOutput);

        Assert.Equal("~/repos/fleet", panes[0].PaneTitle);
        Assert.Equal("~/repos/fleet", panes[0].Title);
        Assert.Equal("claude", panes[1].PaneTitle);
        Assert.Equal("backend", panes[1].Title);
    }

    [Fact]
    public void SplitArgs_move_an_existing_pane_instead_of_spawning_when_asked()
    {
        var args = WezTermDriver.SplitArgs(
            new SplitOptions(new PaneId("4"), SplitDirection.Right)
            {
                Percent = 50,
                Cwd = "C:/ignored",
                Args = ["ignored"],
                MovePane = new PaneId("9"),
            });

        Assert.Equal(
            ["split-pane", "--pane-id", "4", "--right", "--percent", "50", "--move-pane-id", "9"],
            args);
    }

    [Fact]
    public void SplitArgs_spawn_the_command_in_the_cwd_otherwise()
    {
        var args = WezTermDriver.SplitArgs(
            new SplitOptions(new PaneId("4"), SplitDirection.Right) { Cwd = "C:/x", Args = ["nvim"] });

        Assert.Equal(["split-pane", "--pane-id", "4", "--right", "--cwd", "C:/x", "--", "nvim"], args);
    }

    [Fact]
    public void ParsePanes_keeps_the_window_and_the_tab_apart()
    {
        var panes = WezTermDriver.ParsePanes(RealOutput);

        Assert.Equal("58", panes[0].TabId);
        Assert.NotEqual(panes[0].TabId, panes[0].WindowId);
    }

    [Fact]
    public void ParsePanes_normalizes_the_windows_file_url_cwd()
    {
        var panes = WezTermDriver.ParsePanes(RealOutput);

        Assert.Equal("C:/repos/fleet/", panes[0].Cwd);
        Assert.Equal("C:/repos/backend/", panes[1].Cwd);
    }

    [Fact]
    public void ParsePanes_falls_back_to_the_pane_title_when_the_tab_title_is_blank()
    {
        var panes = WezTermDriver.ParsePanes(RealOutput);

        Assert.Equal("~/repos/fleet", panes[0].Title);
        Assert.Equal("backend", panes[1].Title);
    }

    [Fact]
    public void ParsePanes_handles_an_empty_list()
        => Assert.Empty(WezTermDriver.ParsePanes("[]"));

    [Fact]
    public void Moving_a_pane_names_the_target_window_exactly_once()
    {
        var args = WezTermDriver.MoveArgs(
            new PaneId("29"), new MovePaneOptions { WindowId = "14" });

        Assert.Equal(1, args.Count(a => a == "--window-id"));
        Assert.Equal(["move-pane-to-new-tab", "--pane-id", "29", "--window-id", "14"], args);
    }

    [Fact]
    public void Moving_a_pane_to_a_workspace_needs_a_new_window()
    {
        var args = WezTermDriver.MoveArgs(
            new PaneId("29"), new MovePaneOptions { Workspace = "fleet-hidden" });

        Assert.Contains("--new-window", args);
        Assert.Contains("--workspace", args);
        Assert.DoesNotContain("--window-id", args);
    }

    [Fact]
    public void Spawning_into_a_window_names_it_once_too()
    {
        var args = WezTermDriver.SpawnArgs(new SpawnOptions { WindowId = "14", Cwd = "C:/x" });

        Assert.Equal(1, args.Count(a => a == "--window-id"));
    }

    [Fact]
    public void Every_cli_call_refuses_to_start_a_mux_server_of_its_own()
    {
        var argv = WezTermCli.Argv(["list", "--format", "json"]);

        Assert.Equal(["cli", WezTermCli.NoAutoStart, "list", "--format", "json"], argv);
    }
}
