using System.Text;
using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Input;
using Fleet.Platform.Mux.Embedded.Model;
using Fleet.Platform.Mux.Embedded.Native;
using Fleet.Platform.Mux.Embedded.Render;

namespace Fleet.Tests.Platform.Mux.Embedded;

public class ScrollbackTests
{
    private static FakePanes.FakeTerminal History(int lines, int cols = 20, int rows = 5)
    {
        var terminal = new FakePanes.FakeTerminal(cols, rows);
        terminal.Write(Encoding.UTF8.GetBytes(string.Join('\n', Enumerable.Range(1, lines).Select(i => $"line {i}"))));
        return terminal;
    }

    private static ScreenBuffer Shot(IPaneTerminal terminal)
    {
        var screen = new ScreenBuffer();
        terminal.Snapshot(screen);
        return screen;
    }

    [Fact]
    public void Copy_mode_starts_at_the_panes_cursor()
    {
        var terminal = History(30);

        var copy = CopySession.Enter("p1", terminal, Shot(terminal));

        Assert.Equal(new TextPoint(29, 7), copy.Cursor);
        Assert.Null(copy.Anchor);
    }

    [Fact]
    public void Moving_above_the_viewport_scrolls_history_into_view()
    {
        var terminal = History(30);
        var copy = CopySession.Enter("p1", terminal, Shot(terminal));

        for (var i = 0; i < 6; i++)
        {
            copy.Apply("up", terminal, 20);
        }

        Assert.Equal(23, copy.Cursor.Row);
        Assert.Equal(23, terminal.Viewport.Top);
        Assert.False(terminal.Viewport.AtBottom);
        Assert.Equal(new TextPoint(0, 7), copy.Overlay(terminal.Viewport).Cursor);
    }

    [Fact]
    public void Top_bottom_and_line_ends_stay_inside_the_history()
    {
        var terminal = History(30);
        var copy = CopySession.Enter("p1", terminal, Shot(terminal));

        copy.Apply("top", terminal, 20);
        Assert.Equal((new TextPoint(0, 0), 0L), (copy.Cursor, terminal.Viewport.Top));

        copy.Apply("up", terminal, 20);
        copy.Apply("left", terminal, 20);
        Assert.Equal(new TextPoint(0, 0), copy.Cursor);

        copy.Apply("end", terminal, 20);
        copy.Apply("bottom", terminal, 20);
        Assert.Equal(new TextPoint(29, 0), copy.Cursor);
        Assert.True(terminal.Viewport.AtBottom);
    }

    [Fact]
    public void A_selection_is_copied_from_the_anchor_to_the_cursor_and_the_pane_follows_output_again()
    {
        var terminal = History(30);
        var copy = CopySession.Enter("p1", terminal, Shot(terminal));
        copy.Apply("up", terminal, 20);
        copy.Apply("up", terminal, 20);
        copy.Apply("start", terminal, 20);
        copy.Apply("right", terminal, 20);

        copy.Apply("select", terminal, 20);
        copy.Apply("down", terminal, 20);
        copy.Apply("right", terminal, 20);
        var text = copy.Apply("yank", terminal, 20);

        Assert.Equal("ine 28\nlin", text);
        Assert.True(copy.Done);
        Assert.True(terminal.Viewport.AtBottom);
    }

    [Fact]
    public void Yank_without_a_selection_copies_the_cursors_line()
    {
        var terminal = History(30);
        var copy = CopySession.Enter("p1", terminal, Shot(terminal));
        copy.Apply("up", terminal, 20);

        Assert.Equal("line 29", copy.Apply("yank", terminal, 20));
    }

    [Fact]
    public void A_scrolled_back_pane_shows_how_far_back_it_is()
    {
        var model = new MuxModel();
        var pane = model.Spawn("techweb", "C:/x", ["claude"]);
        var client = model.Connect(20, 6, "techweb");
        model.Resizes();
        var terminal = History(30, 20, 5);
        terminal.Scroll(ScrollTo.Delta, -10);
        var screen = Shot(terminal);

        var frame = Composer.Compose(model.View(client.Id)!, id => id == pane.Id ? screen : null, null);

        Assert.EndsWith("[10/25]", frame.RowText(1));
        Assert.StartsWith("line 16", frame.RowText(1));
    }

    [Fact]
    public void Copy_mode_draws_its_cursor_and_inverts_the_selection()
    {
        var model = new MuxModel();
        var pane = model.Spawn("techweb", "C:/x", ["claude"]);
        var client = model.Connect(20, 6, "techweb");
        model.Resizes();
        var screen = Shot(History(30, 20, 5));
        var copy = new CopyOverlay(pane.Id, new TextPoint(1, 2), new TextPoint(0, 18));

        var frame = Composer.Compose(model.View(client.Id)!, id => id == pane.Id ? screen : null, null, copy);

        Assert.Equal((2, 2, true), (frame.CursorX, frame.CursorY, frame.CursorVisible));
        Assert.True(frame.At(18, 1).Attrs.HasFlag(CellAttr.Inverse));
        Assert.True(frame.At(0, 2).Attrs.HasFlag(CellAttr.Inverse));
        Assert.True(frame.At(2, 2).Attrs.HasFlag(CellAttr.Inverse));
        Assert.False(frame.At(3, 2).Attrs.HasFlag(CellAttr.Inverse));
        Assert.False(frame.At(17, 1).Attrs.HasFlag(CellAttr.Inverse));
    }

    [Theory]
    [InlineData("k", 1, "up")]
    [InlineData("G", 1, "bottom")]
    [InlineData("$", 1, "end")]
    [InlineData("v", 1, "select")]
    [InlineData("\u0015", 1, "halfup")]
    [InlineData("\e[B", 3, "down")]
    [InlineData("\e[5~", 4, "pageup")]
    [InlineData("\e[H", 3, "start")]
    public void Copy_mode_reads_keys_from_a_unix_terminal(string typed, int consumed, string step)
    {
        var mode = new CopyMode();
        mode.Enter();

        Assert.Equal(consumed, mode.OnBytes(Encoding.ASCII.GetBytes(typed + "zz"), out var command));
        Assert.Equal(("copy", step), (command!.Name, command.Arg));
        Assert.True(mode.Active);
    }

    [Theory]
    [InlineData("y", "yank")]
    [InlineData("q", "exit")]
    [InlineData("\r", "yank")]
    [InlineData("\e", "exit")]
    public void Yank_and_quit_leave_copy_mode(string typed, string step)
    {
        var mode = new CopyMode();
        mode.Enter();

        mode.OnBytes(Encoding.ASCII.GetBytes(typed), out var command);

        Assert.Equal(step, command!.Arg);
        Assert.False(mode.Active);
    }

    [Fact]
    public void Copy_mode_reads_windows_keys_by_their_text_and_ctrl_chords()
    {
        var mode = new CopyMode();
        mode.Enter();

        Assert.Equal("end", mode.OnKey(Key.Digit4, Mods.Shift, "$")!.Arg);
        Assert.Equal("halfdown", mode.OnKey(Key.D, Mods.Ctrl)!.Arg);
        Assert.Equal("pagedown", mode.OnKey(Key.PageDown, Mods.None)!.Arg);
        Assert.Null(mode.OnKey(Key.X, Mods.None, "x"));
        Assert.True(mode.Active);
        Assert.Equal("exit", mode.OnKey(Key.Escape, Mods.None)!.Arg);
        Assert.False(mode.Active);
    }

    [Fact]
    public void The_prefix_bracket_enters_copy_mode()
    {
        Assert.Equal("copy-mode", MuxKeys.Defaults.PrefixCommand(Key.BracketLeft, Mods.None, "["));
        Assert.Equal(("copy-mode", 1), MuxKeys.Defaults.PrefixBytes("["u8));
    }
}
