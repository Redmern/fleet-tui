using Fleet.Platform.Mux.WezTerm;

namespace Fleet.Tests.Platform.Mux;

public class WezTermDriverTests
{
    /// <summary>
    /// Captured verbatim from `wezterm cli list --format json` on
    /// wezterm 20260117-154428-05343b38, trimmed to two panes. Includes the fields
    /// fleet ignores, because silently depending on their absence would be a lie.
    /// </summary>
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
    public void ParsePanes_treats_the_tab_as_the_window()
    {
        // WezTerm's "tab" is fleet's "window": one project per tab, split into a
        // harness pane and a dashboard pane. Both panes above share tab 58.
        var panes = WezTermDriver.ParsePanes(RealOutput);

        Assert.Single(panes.Select(p => p.WindowId).Distinct());
        Assert.Equal("58", panes[0].WindowId);
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

        Assert.Equal("~/repos/fleet", panes[0].Title);   // tab_title was ""
        Assert.Equal("backend", panes[1].Title);         // tab_title was set
    }

    [Fact]
    public void ParsePanes_handles_an_empty_list()
        => Assert.Empty(WezTermDriver.ParsePanes("[]"));
}
