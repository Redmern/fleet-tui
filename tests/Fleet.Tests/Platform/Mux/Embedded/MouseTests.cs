using System.Text;
using Fleet.Platform.Mux.Embedded.Host;
using Fleet.Platform.Mux.Embedded.Input;
using Fleet.Platform.Mux.Embedded.Model;
using Fleet.Platform.Mux.Embedded.Protocol;

namespace Fleet.Tests.Platform.Mux.Embedded;

public class MouseTests
{
    private static WindowsConsole.MouseEventRecord Record(int x, int y, uint buttons = 0, uint flags = 0, uint state = 0) =>
        new() { X = (short)x, Y = (short)y, ButtonState = buttons, EventFlags = flags, ControlKeyState = state };

    [Fact]
    public void A_click_becomes_press_then_release_with_the_button_and_cell()
    {
        var mouse = new WindowsMouse();

        var press = mouse.Translate(Record(5, 3, buttons: 1)).Single();
        var release = mouse.Translate(Record(5, 3, buttons: 0)).Single();

        Assert.Equal((MouseButtons.Left, MouseActions.Press, 5, 3, true), (press.Button, press.Action, press.X, press.Y, press.Held));
        Assert.Equal((MouseButtons.Left, MouseActions.Release, false), (release.Button, release.Action, release.Held));
    }

    [Fact]
    public void Coordinates_are_made_relative_to_the_visible_window()
    {
        var press = new WindowsMouse().Translate(Record(12, 1040, buttons: 1), 2, 1030).Single();

        Assert.Equal((10, 10), (press.X, press.Y));
    }

    [Fact]
    public void Dragging_reports_motion_with_the_held_button_once_per_cell()
    {
        var mouse = new WindowsMouse();
        mouse.Translate(Record(1, 1, buttons: 1)).ToList();

        var moved = mouse.Translate(Record(2, 1, buttons: 1, flags: 1)).Single();
        var sameCell = mouse.Translate(Record(2, 1, buttons: 1, flags: 1)).ToList();

        Assert.Equal((MouseButtons.Left, MouseActions.Motion, true), (moved.Button, moved.Action, moved.Held));
        Assert.Empty(sameCell);
    }

    [Fact]
    public void The_wheel_is_a_press_of_wheel_up_or_down()
    {
        var mouse = new WindowsMouse();

        var up = mouse.Translate(Record(0, 0, buttons: 120u << 16, flags: 4)).Single();
        var down = mouse.Translate(Record(0, 0, buttons: unchecked((uint)(-120 << 16)), flags: 4)).Single();

        Assert.Equal(MouseButtons.WheelUp, up.Button);
        Assert.Equal(MouseButtons.WheelDown, down.Button);
        Assert.Equal(MouseActions.Press, down.Action);
    }

    [Fact]
    public void Shift_ctrl_and_alt_become_mouse_mods()
    {
        var press = new WindowsMouse().Translate(Record(0, 0, buttons: 1, state: 0x10 | 0x8 | 0x2)).Single();

        Assert.Equal(1 | 2 | 4, press.Mods);
    }

    [Fact]
    public void Sgr_reports_are_taken_out_of_the_byte_stream_and_the_rest_left_in_order()
    {
        var items = new SgrMouse().Feed(Encoding.ASCII.GetBytes("ab\e[<0;10;5Mcd\e[<0;10;5m")).ToList();

        Assert.Equal("ab", Encoding.ASCII.GetString((byte[])items[0]));
        var press = (MouseMessage)items[1];
        Assert.Equal((MouseButtons.Left, MouseActions.Press, 9, 4), (press.Button, press.Action, press.X, press.Y));
        Assert.Equal("cd", Encoding.ASCII.GetString((byte[])items[2]));
        Assert.Equal(MouseActions.Release, ((MouseMessage)items[3]).Action);
    }

    [Fact]
    public void An_sgr_report_split_across_reads_is_joined()
    {
        var parser = new SgrMouse();

        var first = parser.Feed(Encoding.ASCII.GetBytes("x\e[<64;3")).ToList();
        var second = parser.Feed(Encoding.ASCII.GetBytes(";7M")).ToList();

        Assert.Equal("x", Encoding.ASCII.GetString((byte[])first.Single()));
        Assert.Equal(MouseButtons.WheelUp, ((MouseMessage)second.Single()).Button);
    }

    [Fact]
    public void A_lone_escape_at_the_end_of_a_read_is_passed_on_not_held()
    {
        var items = new SgrMouse().Feed("\e"u8).ToList();

        Assert.Equal([0x1b], (byte[])items.Single());
    }

    [Fact]
    public void Other_escape_sequences_pass_through_untouched()
    {
        var items = new SgrMouse().Feed(Encoding.ASCII.GetBytes("\e[A\e[<5x")).ToList();

        Assert.Equal("\e[A\e[<5x", string.Concat(items.Cast<byte[]>().Select(b => Encoding.ASCII.GetString(b))));
    }

    [Fact]
    public void Sgr_motion_with_a_held_button_and_hover_without_one_are_told_apart()
    {
        var items = new SgrMouse().Feed(Encoding.ASCII.GetBytes("\e[<32;4;4M\e[<35;5;4M")).Cast<MouseMessage>().ToList();

        Assert.Equal((MouseButtons.Left, MouseActions.Motion, true), (items[0].Button, items[0].Action, items[0].Held));
        Assert.Equal((MouseButtons.None, MouseActions.Motion, false), (items[1].Button, items[1].Action, items[1].Held));
    }

    [Fact]
    public void The_model_finds_the_pane_divider_and_status_bar_under_the_pointer()
    {
        var model = new MuxModel();
        var claude = model.Spawn("techweb", "C:/x", ["claude"]);
        var dash = model.Split(claude.Id, true, false, 50, "C:/x", ["fleet"])!;
        var client = model.Connect(21, 6, "techweb");
        var divider = model.View(client.Id)!.Dividers.Single().X;

        var left = model.Hit(client.Id, 2, 2);
        var right = model.Hit(client.Id, divider + 3, 3);

        Assert.Equal((MouseHitKind.Pane, claude.Id, 2, 1), (left.Kind, left.Pane, left.X, left.Y));
        Assert.Equal((MouseHitKind.Pane, dash.Id, 2, 2), (right.Kind, right.Pane, right.X, right.Y));
        Assert.Equal(MouseHitKind.Divider, model.Hit(client.Id, divider, 1).Kind);
        Assert.Equal(MouseHitKind.StatusBar, model.Hit(client.Id, 0, 0).Kind);
    }

    [Fact]
    public void Dragging_a_divider_moves_it_and_resizes_both_panes()
    {
        var model = new MuxModel();
        var claude = model.Spawn("techweb", "C:/x", ["claude"]);
        var dash = model.Split(claude.Id, true, false, 50, "C:/x", ["fleet"])!;
        var client = model.Connect(41, 6, "techweb");
        model.Resizes();
        var before = claude.Cols;

        Assert.True(model.DragDivider(client.Id, 0, 30, 2));
        model.Resizes();

        Assert.True(claude.Cols > before);
        Assert.Equal(41, claude.Cols + 1 + dash.Cols);
        Assert.Equal(claude.Cols, model.View(client.Id)!.Dividers.Single().X);
    }

    [Fact]
    public void A_relative_position_is_clamped_to_the_pane_while_dragging_outside_it()
    {
        var model = new MuxModel();
        var claude = model.Spawn("techweb", "C:/x", ["claude"]);
        model.Split(claude.Id, true, false, 50, "C:/x", ["fleet"]);
        var client = model.Connect(21, 6, "techweb");

        var inside = model.Relative(client.Id, claude.Id, 50, 50);

        var area = model.View(client.Id)!.Panes.Single(p => p.Pane == claude.Id).Area;
        Assert.Equal((area.Width - 1, area.Height - 1), inside);
    }
}
