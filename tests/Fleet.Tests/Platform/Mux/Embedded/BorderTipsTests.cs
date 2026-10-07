using Fleet.Platform.Mux.Embedded.Model;
using Fleet.Platform.Mux.Embedded.Render;

namespace Fleet.Tests.Platform.Mux.Embedded;

public sealed class BorderTipsTests
{
    private static readonly FloatButton Help = new(false, false, string.Empty, "i", "f1", "keybinds");
    private static readonly FloatButton Close = new(false, true, string.Empty, "x", "f2", "close");
    private static readonly FloatButton Open = new(true, true, "enter", "o", "f3", "open");
    private static readonly FloatButton Bare = new(true, false, string.Empty, "b", "f4");

    private readonly MuxModel _model = new();

    private (ClientView View, Rect Area, string Pane) Float(params FloatButton[] buttons)
    {
        _model.Spawn("techweb", "C:/x", ["claude"]);
        var client = _model.Connect(100, 41, "techweb");
        var box = _model.SpawnFloat("techweb", "C:/x", ["fleet", "menu"]);
        Assert.True(_model.SetFloatButtons(box.Id, buttons));
        _model.Resizes();
        var view = _model.View(client.Id)!;
        return (view, view.FloatingPanes.Single().Area, box.Id);
    }

    private static PlacedButton PlacedOf(Rect area, IReadOnlyList<FloatButton> buttons, FloatButton button) =>
        BorderButtons.Place(area, buttons).Single(p => p.Button == button);

    [Fact]
    public void Hovering_a_top_edge_button_puts_its_tip_on_the_row_below_it()
    {
        var (view, area, pane) = Float(Help, Close, Open);
        var help = PlacedOf(area, [Help, Close, Open], Help);

        var tip = BorderTips.At(view, help.X + 1, help.Y);

        Assert.Equal(new BorderTip(pane, " keybinds ", help.X, area.Y + 1), tip);
    }

    [Fact]
    public void Hovering_a_bottom_edge_button_puts_its_tip_on_the_row_above_it()
    {
        var (view, area, _) = Float(Help, Close, Open);
        var open = PlacedOf(area, [Help, Close, Open], Open);

        var tip = BorderTips.At(view, open.X + open.Width - 1, open.Y);

        Assert.Equal(" open ", tip!.Value.Text);
        Assert.Equal(area.Y + area.Height - 2, tip.Value.Y);
    }

    [Fact]
    public void The_plain_border_a_button_without_a_tip_and_the_inside_of_the_float_show_nothing()
    {
        var (view, area, _) = Float(Help, Bare);
        var bare = PlacedOf(area, [Help, Bare], Bare);

        Assert.Null(BorderTips.At(view, area.X + area.Width / 2, area.Y));
        Assert.Null(BorderTips.At(view, bare.X + 1, bare.Y));
        Assert.Null(BorderTips.At(view, area.X + 3, area.Y + 2));
    }

    [Fact]
    public void A_tip_near_the_right_edge_moves_left_to_stay_on_screen()
    {
        var placed = new PlacedButton(Close with { Tip = "close this window" }, 95, 0, 5);

        var tip = BorderTips.Place("p", placed, 100, 41);

        Assert.Equal(100 - " close this window ".Length, tip!.Value.X);
        Assert.Equal(1, tip.Value.Y);
        Assert.Null(BorderTips.Place("p", placed with { Y = 40 }, 100, 41));
    }

    [Fact]
    public void A_dash_frame_button_has_a_tip_too()
    {
        var claude = _model.Spawn("alpha", "C:/repos/alpha", ["nvim"]);
        var dash = _model.Split(claude.Id, sideBySide: true, newFirst: false, 50, "C:/repos/alpha", ["C:/tools/fleet.exe", "dash"])!;
        var client = _model.Connect(60, 20, "alpha");
        Assert.True(_model.SetFloatButtons(dash.Id, [Help]));
        _model.Resizes();
        var view = _model.View(client.Id)!;
        var around = ClientView.Around(view.Panes.Single(p => p.Pane == dash.Id).Area);
        var help = PlacedOf(around, [Help], Help);

        var tip = BorderTips.At(view, help.X + 1, help.Y);

        Assert.Equal((dash.Id, " keybinds ", help.Y + 1), (tip!.Value.Pane, tip.Value.Text, tip.Value.Y));
    }

    [Fact]
    public void The_composer_draws_the_tip_in_the_chip_colors()
    {
        var (view, area, _) = Float(Help, Close, Open);
        var help = PlacedOf(area, [Help, Close, Open], Help);
        var tip = BorderTips.At(view, help.X + 1, help.Y);

        var frame = Composer.Compose(view, _ => null, null, tip: tip);
        var plain = Composer.Compose(view, _ => null, null);

        Assert.Equal(" keybinds ", frame.RowText(area.Y + 1)[help.X..(help.X + 10)]);
        var cell = frame.At(help.X + 1, area.Y + 1);
        Assert.Equal((Composer.Text, Composer.Surface0), (cell.Fg, cell.Bg));
        Assert.DoesNotContain("keybinds", plain.RowText(area.Y + 1), StringComparison.Ordinal);
    }
}
