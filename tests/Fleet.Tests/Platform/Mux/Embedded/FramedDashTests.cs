using Fleet.Platform.Mux.Embedded.Model;
using Fleet.Platform.Mux.Embedded.Render;

namespace Fleet.Tests.Platform.Mux.Embedded;

public sealed class FramedDashTests
{
    private readonly MuxModel _model = new();

    private (PaneState Claude, PaneState Dash, ClientState Client) Dash(int cols = 60, int rows = 20)
    {
        var claude = _model.Spawn("alpha", "C:/repos/alpha", ["nvim"]);
        var dash = _model.Split(claude.Id, sideBySide: true, newFirst: false, 50, "C:/repos/alpha", ["C:/tools/fleet.exe", "dash", "--project", "alpha"])!;
        return (claude, dash, _model.Connect(cols, rows, "alpha"));
    }

    [Fact]
    public void The_dash_tab_is_framed_and_its_panes_sit_inside_the_frame()
    {
        var (claude, dash, client) = Dash();
        _model.Resizes();

        var view = _model.View(client.Id)!;
        var content = MuxModel.Content(60, 20);

        Assert.Equal(content, view.Frame);
        Assert.Equal("fleet — alpha", view.FrameTitle);
        var left = view.Panes.Single(p => p.Pane == claude.Id).Area;
        var right = view.Panes.Single(p => p.Pane == dash.Id).Area;
        Assert.Equal((content.X + 1, content.Y + 1), (left.X, left.Y));
        Assert.Equal(content.X + content.Width - 1, right.X + right.Width);
        Assert.Equal(content.Height - 2, left.Height);
        Assert.Equal((left.Width, left.Height), (claude.Cols, claude.Rows));
    }

    [Fact]
    public void Other_tabs_are_not_framed()
    {
        var shell = _model.Spawn("alpha", "C:/repos/alpha", ["pwsh"]);
        var client = _model.Connect(60, 20, "alpha");

        var view = _model.View(client.Id)!;

        Assert.Null(view.Frame);
        Assert.Equal(MuxModel.Content(60, 20), view.Panes.Single(p => p.Pane == shell.Id).Area);
    }

    [Fact]
    public void The_frame_joins_the_divider_carries_the_title_and_lights_the_focused_side()
    {
        var (_, dash, client) = Dash();
        var view = _model.View(client.Id)!;
        var box = view.Frame!.Value;
        var divider = view.Dividers.Single();

        var frame = Composer.Compose(view, _ => null, null);

        var top = box.Y;
        var bottom = box.Y + box.Height - 1;
        Assert.Equal("╭", frame.At(box.X, top).Text);
        Assert.Equal("╯", frame.At(box.X + box.Width - 1, bottom).Text);
        Assert.Equal("┬", frame.At(divider.X, top).Text);
        Assert.Equal("┴", frame.At(divider.X, bottom).Text);
        Assert.Contains("fleet — alpha", frame.RowText(top), StringComparison.Ordinal);

        Assert.Equal(dash.Id, view.Focused);
        Assert.Equal(Composer.FocusFg, frame.At(box.X + box.Width - 1, top + 3).Fg);
        Assert.Equal(Composer.DividerFg, frame.At(box.X, top + 3).Fg);
    }

    [Fact]
    public void The_line_under_the_tab_bar_is_empty_and_not_clickable()
    {
        var (_, _, client) = Dash();

        Assert.Equal(MouseHitKind.StatusBar, _model.Hit(client.Id, 3, 0).Kind);
        Assert.Equal(MouseHitKind.None, _model.Hit(client.Id, 3, MuxModel.BarRows).Kind);
        Assert.Equal(MuxModel.BarRows + 1, MuxModel.Content(60, 20).Y);
    }
}
