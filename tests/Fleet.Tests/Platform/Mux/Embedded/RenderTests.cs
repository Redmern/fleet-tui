using Fleet.Platform.Mux.Embedded.Model;
using Fleet.Platform.Mux.Embedded.Render;

namespace Fleet.Tests.Platform.Mux.Embedded;

public class RenderTests
{
    private readonly MuxModel _model = new();
    private readonly Dictionary<string, ScreenBuffer> _screens = [];

    private ScreenBuffer Screen(PaneState pane, string text)
    {
        var screen = new ScreenBuffer();
        screen.Resize(pane.Cols, pane.Rows);
        screen.Write(0, 0, text);
        screen.CursorX = text.Length;
        _screens[pane.Id] = screen;
        return screen;
    }

    private ClientFrame Compose(string client, string? badge = null) =>
        Composer.Compose(_model.View(client)!, id => _screens.GetValueOrDefault(id), badge);

    [Fact]
    public void Two_panes_are_drawn_side_by_side_with_a_divider_and_a_status_bar()
    {
        var claude = _model.Spawn("techweb", "C:/x", ["claude"]);
        var dash = _model.Split(claude.Id, true, false, 50, "C:/x", ["fleet"])!;
        _model.SetTitle(claude.Id, "dashboard");
        var client = _model.Connect(21, 5, "techweb");
        _model.Resizes();
        Screen(claude, "left");
        Screen(dash, "right");

        var frame = Compose(client.Id);

        Assert.StartsWith("left", frame.RowText(0));
        Assert.Equal('│', frame.At(claude.Cols, 0).Text[0]);
        Assert.Equal("right", frame.RowText(0)[(claude.Cols + 1)..].TrimEnd());
        Assert.Contains("techweb", frame.RowText(4));
        Assert.Contains("1:dashboard", frame.RowText(4));
    }

    [Fact]
    public void The_cursor_is_the_focused_panes_cursor_offset_by_its_position()
    {
        var claude = _model.Spawn("techweb", "C:/x", ["claude"]);
        var dash = _model.Split(claude.Id, true, false, 50, "C:/x", ["fleet"])!;
        var client = _model.Connect(21, 5, "techweb");
        _model.Resizes();
        Screen(claude, "left");
        Screen(dash, "ab");

        var frame = Compose(client.Id);

        Assert.Equal(claude.Cols + 1 + 2, frame.CursorX);
        Assert.Equal(0, frame.CursorY);
        Assert.True(frame.CursorVisible);
    }

    [Fact]
    public void A_badge_is_drawn_at_the_right_of_the_status_bar()
    {
        _model.Spawn("techweb", "C:/x", ["claude"]);
        var client = _model.Connect(30, 4, "techweb");

        var frame = Compose(client.Id, "ctrl+b");

        Assert.EndsWith(" ctrl+b ", frame.RowText(3));
    }

    [Fact]
    public void An_unchanged_frame_encodes_to_cursor_bytes_only()
    {
        var pane = _model.Spawn("techweb", "C:/x", ["claude"]);
        var client = _model.Connect(20, 4, "techweb");
        _model.Resizes();
        Screen(pane, "hello");
        var first = Compose(client.Id);

        var again = FrameEncoder.Encode(first, Compose(client.Id));

        Assert.DoesNotContain("hello", again);
        Assert.DoesNotContain("\e[2J", again);
    }

    [Fact]
    public void A_single_changed_cell_is_the_only_text_sent()
    {
        var pane = _model.Spawn("techweb", "C:/x", ["claude"]);
        var client = _model.Connect(20, 4, "techweb");
        _model.Resizes();
        var screen = Screen(pane, "hello");
        var first = Compose(client.Id);

        screen.Write(1, 0, "a");
        var diff = FrameEncoder.Encode(first, Compose(client.Id));

        Assert.Contains("\e[1;2H", diff);
        Assert.DoesNotContain("hello", diff);
        Assert.DoesNotContain("hallo", diff);
    }

    [Fact]
    public void The_first_frame_is_a_full_redraw()
    {
        var pane = _model.Spawn("techweb", "C:/x", ["claude"]);
        var client = _model.Connect(20, 4, "techweb");
        _model.Resizes();
        Screen(pane, "hello");

        var full = FrameEncoder.Encode(null, Compose(client.Id));

        Assert.Contains("\e[2J", full);
        Assert.Contains("hello", full);
    }

    [Fact]
    public void Switching_workspaces_draws_the_other_workspace_from_its_existing_screen()
    {
        var techweb = _model.Spawn("techweb", "C:/x", ["claude"]);
        var fleet = _model.Spawn("fleet", "C:/y", ["claude"]);
        var client = _model.Connect(20, 4, "techweb");
        _model.Resizes();
        Screen(techweb, "tw");
        var fleetScreen = Screen(fleet, "fl");
        var shown = Compose(client.Id);
        var version = fleetScreen.Version;

        _model.Show(client.Id, "fleet");
        var next = Compose(client.Id);

        Assert.StartsWith("fl", next.RowText(0));
        Assert.Contains("fl", FrameEncoder.Encode(shown, next));
        Assert.Equal(version, fleetScreen.Version);
    }
}
