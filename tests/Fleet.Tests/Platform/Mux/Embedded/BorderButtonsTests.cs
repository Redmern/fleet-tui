using System.Text;
using Fleet.Platform.Mux.Embedded.Input;
using Fleet.Platform.Mux.Embedded.Model;
using Fleet.Platform.Mux.Embedded.Native;
using Fleet.Platform.Mux.Embedded.Render;

namespace Fleet.Tests.Platform.Mux.Embedded;

public class BorderButtonsTests
{
    private static readonly FloatButton Help = new(false, false, "?", "i", "f1");
    private static readonly FloatButton Close = new(false, true, "esc", "x", "esc");
    private static readonly FloatButton Select = new(true, true, "enter", "s", "enter");
    private static readonly FloatButton Back = new(true, true, "bksp", "b", "backspace");

    private readonly MuxModel _model = new();

    [Fact]
    public void A_button_is_a_pill_as_wide_as_its_key_and_label_plus_the_caps()
    {
        Assert.Equal(" ? i ", BorderButtons.Text(Help));
        Assert.Equal(7, BorderButtons.Width(Help));
        Assert.Equal(" x ", BorderButtons.Text(Close with { Key = string.Empty }));
    }

    [Fact]
    public void Left_buttons_start_two_columns_in_and_right_buttons_end_two_columns_from_the_corner()
    {
        var box = new Rect(10, 5, 40, 10);

        var placed = BorderButtons.Place(box, [Help, Close, Select, Back]);

        Assert.Equal((12, 5), (placed.Single(p => p.Button == Help).X, placed.Single(p => p.Button == Help).Y));
        var close = placed.Single(p => p.Button == Close);
        Assert.Equal((48, 5), (close.X + close.Width, close.Y));

        var select = placed.Single(p => p.Button == Select);
        var back = placed.Single(p => p.Button == Back);
        Assert.Equal(14, select.Y);
        Assert.Equal(48, back.X + back.Width);
        Assert.Equal(back.X - BorderButtons.Gap, select.X + select.Width);
    }

    [Fact]
    public void A_button_that_does_not_fit_is_dropped_and_the_rest_keep_their_order()
    {
        var box = new Rect(0, 0, 20, 5);

        var placed = BorderButtons.Place(box, [Select, Back]);

        Assert.Equal([Select], placed.Select(p => p.Button));
        Assert.Equal(18, placed[0].X + placed[0].Width);
    }

    [Fact]
    public void The_title_goes_between_the_top_buttons()
    {
        var box = new Rect(0, 0, 40, 10);
        var placed = BorderButtons.Place(box, [Help, Close]);

        var (from, to) = BorderButtons.TitleRoom(box, placed);

        Assert.Equal(2 + BorderButtons.Width(Help) + BorderButtons.Gap, from);
        Assert.Equal(placed.Single(p => p.Button == Close).X - BorderButtons.Gap, to);
    }

    [Fact]
    public void A_cell_on_a_button_finds_it_and_a_cell_on_the_plain_border_does_not()
    {
        var box = new Rect(0, 0, 40, 10);

        Assert.Equal(Help, BorderButtons.At(box, [Help, Close], 3, 0));
        Assert.Equal(Close, BorderButtons.At(box, [Help, Close], 37, 0));
        Assert.Null(BorderButtons.At(box, [Help, Close], 20, 0));
        Assert.Null(BorderButtons.At(box, [Help, Close], 3, 9));
    }

    [Fact]
    public void A_float_with_buttons_draws_them_as_pills_in_its_border_and_its_title_after_the_left_ones()
    {
        _model.Spawn("techweb", "C:/x", ["claude"]);
        var client = _model.Connect(100, 41, "techweb");
        var box = _model.SpawnFloat("techweb", "C:/x", ["fleet", "menu"]);
        _model.SetTitle(box.Id, "fleet menu");
        Assert.True(_model.SetFloatButtons(box.Id, [Help, Close, Select]));
        _model.Resizes();

        var view = _model.View(client.Id)!;
        var area = view.FloatingPanes.Single().Area;
        var frame = Composer.Compose(view, _ => null, null);

        var top = frame.RowText(area.Y)[area.X..(area.X + area.Width)];
        var bottom = frame.RowText(area.Y + area.Height - 1)[area.X..(area.X + area.Width)];
        Assert.StartsWith($"╭─{Composer.LeftCap} ? i {Composer.RightCap}─ fleet menu ", top);
        Assert.EndsWith($"{Composer.LeftCap} esc x {Composer.RightCap}─╮", top);
        Assert.EndsWith($"{Composer.LeftCap} enter s {Composer.RightCap}─╯", bottom);

        var key = frame.At(area.X + 4, area.Y);
        Assert.Equal((Composer.Blue, Composer.Surface0), (key.Fg, key.Bg));
        var label = frame.At(area.X + 6, area.Y);
        Assert.Equal((Composer.Text, Composer.Surface0), (label.Fg, label.Bg));
        Assert.Equal(Composer.Surface0, frame.At(area.X + 2, area.Y).Fg);
    }

    [Fact]
    public void A_float_without_buttons_keeps_its_plain_titled_border()
    {
        _model.Spawn("techweb", "C:/x", ["claude"]);
        var client = _model.Connect(100, 41, "techweb");
        var box = _model.SpawnFloat("techweb", "C:/x", ["pwsh"]);
        _model.SetTitle(box.Id, "shell");
        _model.Resizes();

        var view = _model.View(client.Id)!;
        var area = view.FloatingPanes.Single().Area;
        var frame = Composer.Compose(view, _ => null, null);

        Assert.StartsWith("╭─ shell ─", frame.RowText(area.Y)[area.X..]);
        Assert.Empty(view.ButtonsOf(box.Id));
    }

    [Fact]
    public void Buttons_only_go_on_floats()
    {
        var tile = _model.Spawn("techweb", "C:/x", ["claude"]);

        Assert.False(_model.SetFloatButtons(tile.Id, [Close]));
    }

    [Fact]
    public void A_button_key_that_does_not_parse_is_no_chord()
    {
        Assert.Null(BorderKeys.Chord("o/enter/A-Z"));
        Assert.NotNull(BorderKeys.Chord("shift+enter"));
    }

    [Fact]
    public void A_named_key_is_a_win32_press_and_release_and_a_character_is_left_to_the_encoder()
    {
        var shiftEnter = BorderKeys.Win32(BorderKeys.Chord("shift+enter")!.Value)!;
        var esc = BorderKeys.Win32(BorderKeys.Chord("esc")!.Value)!;

        Assert.Equal("\e[13;0;13;1;16;1_\e[13;0;13;0;16;1_", Encoding.ASCII.GetString(shiftEnter));
        Assert.Equal("\e[27;0;27;1;0;1_\e[27;0;27;0;0;1_", Encoding.ASCII.GetString(esc));
        Assert.Null(BorderKeys.Win32(BorderKeys.Chord("?")!.Value));
    }

    [Fact]
    public void A_chord_becomes_a_key_press_message()
    {
        var message = BorderKeys.Message(BorderKeys.Chord("ctrl+r")!.Value);

        Assert.Equal(((int)Key.R, (int)Mods.Ctrl, 1), (message.Key, message.Mods, message.Action));
        Assert.Equal("?", BorderKeys.Message(BorderKeys.Chord("?")!.Value).Text);
    }
}
