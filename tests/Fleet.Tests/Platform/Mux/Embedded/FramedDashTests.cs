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
    public void After_a_glyph_terminals_measure_differently_the_next_cell_is_placed_explicitly()
    {
        var frame = new ClientFrame(6, 2);
        var row = "abc";
        for (var x = 0; x < row.Length; x++)
        {
            frame.Cells[x] = Cell.Of(row[x], Composer.DividerFg);
        }

        for (var x = 0; x < 4; x++)
        {
            frame.Cells[6 + x] = Cell.Of("abcd"[x], Composer.DividerFg);
        }

        var encoded = FrameEncoder.Encode(null, frame);

        Assert.Contains("\e[1;3Hb", encoded, StringComparison.Ordinal);
        Assert.Contains("abcd", encoded, StringComparison.Ordinal);
        Assert.True(FrameEncoder.WidthIsDisputed(""));
        Assert.True(FrameEncoder.WidthIsDisputed("😀"));
        Assert.False(FrameEncoder.WidthIsDisputed("│"));
        Assert.False(FrameEncoder.WidthIsDisputed("a"));
    }

    [Fact]
    public void The_frame_starts_right_under_the_tab_bar()
    {
        var (_, _, client) = Dash();

        Assert.Equal(MouseHitKind.StatusBar, _model.Hit(client.Id, 3, 0).Kind);
        Assert.Equal(MuxModel.StatusRows, _model.View(client.Id)!.Frame!.Value.Y);
    }

    [Fact]
    public void The_dash_pane_takes_border_buttons_and_other_tiled_panes_do_not()
    {
        var (claude, dash, client) = Dash();
        FloatButton[] buttons = [new(false, false, "?", "i", "f1"), new(true, true, "n", "+", "f2")];

        Assert.True(_model.SetFloatButtons(dash.Id, buttons));
        Assert.False(_model.SetFloatButtons(claude.Id, buttons));

        var view = _model.View(client.Id)!;
        Assert.Equal(buttons, view.FrameButtonsOf(dash.Id));
        Assert.Empty(view.FrameButtonsOf(claude.Id));
    }

    [Fact]
    public void The_dash_buttons_sit_on_its_own_edges_and_the_title_keeps_its_place()
    {
        var (_, dash, client) = Dash();
        _model.SetFloatButtons(dash.Id, [new(false, false, "?", "i", "f1"), new(true, true, "n", "+", "f2")]);
        var view = _model.View(client.Id)!;
        var box = view.Frame!.Value;
        var around = ClientView.Around(view.Panes.Single(p => p.Pane == dash.Id).Area);

        var frame = Composer.Compose(view, _ => null, null);

        var top = frame.RowText(box.Y);
        Assert.Contains("fleet — alpha", top, StringComparison.Ordinal);
        Assert.Equal(" ", frame.At(around.X + BorderButtons.Inset + 1, box.Y).Text);
        Assert.Equal("?", frame.At(around.X + BorderButtons.Inset + 2, box.Y).Text);
        Assert.Contains(" n + ", frame.RowText(box.Y + box.Height - 1), StringComparison.Ordinal);
    }

    [Fact]
    public void A_dash_alone_in_its_tab_moves_the_title_past_its_top_left_button()
    {
        var dash = _model.Spawn("alpha", "C:/repos/alpha", ["C:/tools/fleet.exe", "dash", "--project", "alpha"]);
        var client = _model.Connect(60, 20, "alpha");
        _model.SetFloatButtons(dash.Id, [new(false, false, "?", "i", "f1")]);
        var view = _model.View(client.Id)!;
        var box = view.Frame!.Value;

        var top = Composer.Compose(view, _ => null, null).RowText(box.Y);

        Assert.True(top.IndexOf(" ? i ", StringComparison.Ordinal) < top.IndexOf("fleet — alpha", StringComparison.Ordinal));
    }
    [Fact]
    public void A_lifted_dash_floats_centred_on_the_whole_window_sized_to_its_dialog()
    {
        var (claude, dash, client) = Dash(cols: 100, rows: 31);
        _model.Resizes();
        var claudeArea = _model.View(client.Id)!.Panes.Single(p => p.Pane == claude.Id).Area;

        Assert.True(_model.Lift(dash.Id, 60, 12, "Base branch - alpha"));
        var changed = _model.Resizes();

        var view = _model.View(client.Id)!;
        var box = view.FloatingPanes.Single();
        Assert.Equal(dash.Id, box.Pane);
        Assert.Equal(MuxModel.Centred(62, 14, MuxModel.Content(100, 31)), box.Area);
        Assert.True(box.Area.X < claudeArea.X + claudeArea.Width, "the dialog reaches over the Claude pane");
        Assert.Equal((60, 12), (dash.Cols, dash.Rows));
        Assert.Contains(changed, c => c.Pane == dash);
        Assert.DoesNotContain(changed, c => c.Pane == claude);
        Assert.Equal(claudeArea, view.Panes.Single().Area);
        Assert.Equal("Base branch - alpha", view.FloatLabel(dash.Id));
        Assert.Equal(dash.Id, view.Focused);
    }

    [Fact]
    public void Dropping_the_lift_puts_the_dash_back_in_its_tile_with_its_size_and_the_split_untouched()
    {
        var (claude, dash, client) = Dash(cols: 100, rows: 31);
        _model.Resizes();
        var before = _model.View(client.Id)!;
        var tile = (dash.Cols, dash.Rows);

        _model.Lift(dash.Id, 60, 12, "Base branch - alpha");
        _model.Resizes();
        Assert.True(_model.Drop(dash.Id));
        _model.Resizes();

        var after = _model.View(client.Id)!;
        Assert.Empty(after.FloatingPanes);
        Assert.Equal(before.Panes, after.Panes);
        Assert.Equal(before.Dividers.Single().X, after.Dividers.Single().X);
        Assert.Equal(tile, (dash.Cols, dash.Rows));
        Assert.Equal(dash.Id, after.Focused);
        Assert.False(_model.Drop(dash.Id));
        Assert.Contains(claude.Id, after.Panes.Select(p => p.Pane));
    }

    [Fact]
    public void A_nested_dialog_relifts_to_its_own_size()
    {
        var (_, dash, client) = Dash(cols: 100, rows: 31);

        _model.Lift(dash.Id, 60, 12, "New agent");
        _model.Lift(dash.Id, 40, 6, "Base branch");
        _model.Resizes();

        Assert.Equal((40, 6), (dash.Cols, dash.Rows));
        Assert.Equal("Base branch", _model.View(client.Id)!.FloatLabel(dash.Id));
    }

    [Fact]
    public void A_resized_window_recentres_the_lift_and_clamps_it_to_the_window()
    {
        var (_, dash, client) = Dash(cols: 100, rows: 31);
        _model.Lift(dash.Id, 90, 20, "New agent");
        _model.Resizes();

        _model.Resize(client.Id, 50, 15);
        _model.Resizes();

        var content = MuxModel.Content(50, 15);
        Assert.Equal(content, _model.View(client.Id)!.FloatingPanes.Single().Area);
        Assert.Equal((48, 12), (dash.Cols, dash.Rows));
    }

    [Fact]
    public void Only_a_tiled_dash_lifts()
    {
        var (claude, _, _) = Dash();
        var box = _model.SpawnFloat("alpha", "C:/repos/alpha", ["C:/tools/fleet.exe", "dash"]);

        Assert.False(_model.Lift(claude.Id, 40, 6, "x"));
        Assert.False(_model.Lift(box.Id, 40, 6, "x"));
        Assert.False(_model.Lift("p404", 40, 6, "x"));
    }

    [Fact]
    public void A_dash_that_dies_while_lifted_leaves_the_rest_of_the_layout_whole()
    {
        var (claude, dash, client) = Dash(cols: 100, rows: 31);
        _model.Lift(dash.Id, 60, 12, "New agent");
        _model.Resizes();

        _model.Kill(dash.Id);
        _model.Resizes();

        var view = _model.View(client.Id)!;
        Assert.Empty(view.FloatingPanes);
        Assert.Equal(claude.Id, view.Panes.Single().Pane);
        Assert.Equal(claude.Id, view.Focused);
    }

    [Fact]
    public void Clicks_inside_the_lift_reach_the_dash_in_its_own_coordinates_and_its_buttons_sit_on_the_lift_border()
    {
        var (_, dash, client) = Dash(cols: 100, rows: 31);
        FloatButton[] buttons = [new(false, true, "esc", "x", "f2")];
        _model.SetFloatButtons(dash.Id, buttons);
        _model.Lift(dash.Id, 60, 12, "New agent");
        var view = _model.View(client.Id)!;
        var area = view.FloatingPanes.Single().Area;

        var hit = _model.Hit(client.Id, area.X + 1, area.Y + 1);

        Assert.Equal((MouseHitKind.Pane, dash.Id, 0, 0), (hit.Kind, hit.Pane, hit.X, hit.Y));
        Assert.Equal(buttons, view.ButtonsOf(dash.Id));
        Assert.Empty(view.FrameButtonsOf(dash.Id));
        Assert.False(_model.MoveFloat(dash.Id, 0, 0));
    }

    [Fact]
    public void The_lift_is_drawn_bordered_with_its_title_over_the_claude_pane()
    {
        var (_, dash, client) = Dash(cols: 100, rows: 31);
        _model.Lift(dash.Id, 60, 12, "Base branch - alpha");
        var view = _model.View(client.Id)!;
        var area = view.FloatingPanes.Single().Area;

        var frame = Composer.Compose(view, _ => null, null);

        Assert.Equal("╭", frame.At(area.X, area.Y).Text);
        Assert.Equal("╯", frame.At(area.X + area.Width - 1, area.Y + area.Height - 1).Text);
        Assert.Contains("Base branch - alpha", frame.RowText(area.Y), StringComparison.Ordinal);
        Assert.Equal(Composer.FocusFg, frame.At(area.X, area.Y + 1).Fg);
    }
}
