using Fleet.Platform.Mux.Embedded.Model;
using Fleet.Platform.Mux.Embedded.Render;

namespace Fleet.Tests.Platform.Mux.Embedded;

public class FloatTests
{
    private readonly MuxModel _model = new();

    private (PaneState Tile, ClientState Client) Workspace(string name, int cols = 100, int rows = 41)
    {
        var tile = _model.Spawn(name, "C:/x", ["claude"]);
        var client = _model.Connect(cols, rows, name);
        return (tile, client);
    }

    [Fact]
    public void A_float_opens_centred_over_the_tiles_focused_and_sized_to_its_inside()
    {
        var (tile, client) = Workspace("techweb");

        var box = _model.SpawnFloat("techweb", "C:/x", ["pwsh"]);
        _model.Resizes();

        var view = _model.View(client.Id)!;
        var area = view.FloatingPanes.Single().Area;
        Assert.Equal(new Rect(20, 8, 60, 24), area);
        Assert.Equal(box.Id, view.Focused);
        Assert.Equal((58, 22), (box.Cols, box.Rows));
        Assert.Equal(tile.Id, view.Panes.Single().Pane);
        Assert.Equal(MuxModel.FloatTab, _model.ListPanes().Single(p => p.Id.Value == box.Id).TabId);
    }

    [Fact]
    public void Hiding_floats_gives_focus_back_to_the_tiles_and_keeps_the_float_alive()
    {
        var (tile, client) = Workspace("techweb");
        var box = _model.SpawnFloat("techweb", "C:/x", ["pwsh"]);

        Assert.True(_model.ToggleFloats(client.Id));

        var view = _model.View(client.Id)!;
        Assert.Empty(view.FloatingPanes);
        Assert.Equal(tile.Id, view.Focused);
        Assert.Contains(box.Id, _model.PanesIn("techweb"));

        Assert.True(_model.ToggleFloats(client.Id));
        Assert.Equal(box.Id, _model.View(client.Id)!.Focused);
    }

    [Fact]
    public void A_modal_float_shows_over_hidden_floats_without_revealing_them_and_cannot_be_tiled()
    {
        var (tile, client) = Workspace("techweb");
        var box = _model.SpawnFloat("techweb", "C:/x", ["pwsh"]);
        _model.ToggleFloats(client.Id);

        var menu = _model.SpawnFloat("techweb", "C:/x", ["fleet", "menu"], new Rect(5, 5, 40, 10), modal: true);

        var view = _model.View(client.Id)!;
        Assert.Equal([menu.Id], view.FloatingPanes.Select(p => p.Pane));
        Assert.Equal(new Rect(5, 5, 40, 10), view.FloatingPanes.Single().Area);
        Assert.Equal(menu.Id, view.Focused);
        Assert.False(_model.ToTile(menu.Id));

        _model.Kill(menu.Id);

        view = _model.View(client.Id)!;
        Assert.Empty(view.FloatingPanes);
        Assert.Equal(tile.Id, view.Focused);
        Assert.Contains(box.Id, _model.PanesIn("techweb"));
    }

    [Fact]
    public void Only_a_modal_float_is_nothing_to_toggle_or_count()
    {
        var (_, client) = Workspace("techweb", cols: 60, rows: 11);
        _model.SpawnFloat("techweb", "C:/x", ["fleet", "menu"], modal: true);
        _model.Resizes();

        Assert.False(_model.ToggleFloats(client.Id));
        Assert.Null(Composer.FloatSpan(_model.View(client.Id)!));
    }

    [Fact]
    public void A_float_over_a_pane_is_centred_on_it_and_sized_for_a_dialog()
    {
        var (tile, client) = Workspace("techweb");
        var agent = _model.Split(tile.Id, true, false, 50, "C:/x", ["claude"])!;

        var area = _model.PaneArea(agent.Id)!.Value;
        var over = MuxModel.Over(area);

        Assert.Equal(_model.View(client.Id)!.Panes.Single(p => p.Pane == agent.Id).Area, area);
        Assert.Equal((50, 14), (over.Width, over.Height));
        Assert.Equal(area.X + (area.Width - 50) / 2, over.X);
        Assert.Equal(area.Y + (area.Height - 14) / 2, over.Y);
    }

    [Fact]
    public void Toggling_without_floats_does_nothing()
    {
        var (_, client) = Workspace("techweb");

        Assert.False(_model.ToggleFloats(client.Id));
    }

    [Fact]
    public void The_border_moves_the_corner_resizes_and_the_inside_is_the_pane()
    {
        var (tile, client) = Workspace("techweb");
        var box = _model.SpawnFloat("techweb", "C:/x", ["pwsh"]);

        Assert.Equal(new MouseHit(MouseHitKind.FloatMove, box.Id, 30, 8), _model.Hit(client.Id, 30, 8));
        Assert.Equal(MouseHitKind.FloatMove, _model.Hit(client.Id, 20, 15).Kind);
        Assert.Equal(new MouseHit(MouseHitKind.FloatResize, box.Id, 79, 31), _model.Hit(client.Id, 79, 31));
        Assert.Equal(new MouseHit(MouseHitKind.Pane, box.Id, 4, 2), _model.Hit(client.Id, 25, 11));
        Assert.Equal(tile.Id, _model.Hit(client.Id, 5, 5).Pane);
    }

    [Fact]
    public void The_topmost_float_wins_where_floats_overlap()
    {
        var (_, client) = Workspace("techweb");
        _model.SpawnFloat("techweb", "C:/x", ["one"]);
        var second = _model.SpawnFloat("techweb", "C:/x", ["two"]);

        Assert.Equal(second.Id, _model.Hit(client.Id, 40, 20).Pane);
    }

    [Fact]
    public void Focusing_a_float_raises_it_and_focusing_a_tile_lowers_the_floats()
    {
        var (tile, client) = Workspace("techweb");
        var first = _model.SpawnFloat("techweb", "C:/x", ["one"]);
        var second = _model.SpawnFloat("techweb", "C:/x", ["two"]);

        _model.Focus(first.Id);
        Assert.Equal(first.Id, _model.View(client.Id)!.FloatingPanes[^1].Pane);
        Assert.Equal(first.Id, _model.View(client.Id)!.Focused);

        _model.Focus(tile.Id);
        var view = _model.View(client.Id)!;
        Assert.Equal(tile.Id, view.Focused);
        Assert.Equal(2, view.FloatingPanes.Count);
        Assert.NotEqual(second.Id, view.Focused);
    }

    [Fact]
    public void A_moved_or_grown_float_stays_on_screen_and_its_pane_follows()
    {
        var (_, client) = Workspace("techweb");
        var box = _model.SpawnFloat("techweb", "C:/x", ["pwsh"]);

        _model.MoveFloat(box.Id, 90, 35);
        _model.Resizes();
        var area = _model.View(client.Id)!.FloatingPanes.Single().Area;
        Assert.Equal(new Rect(40, 16, 60, 24), area);

        _model.ResizeFloat(box.Id, 2, 1);
        _model.Resizes();
        Assert.Equal((MuxModel.MinFloatWidth - 2, MuxModel.MinFloatHeight - 2), (box.Cols, box.Rows));

        _model.ResizeFloat(box.Id, 500, 500);
        _model.Resizes();
        Assert.Equal(new Rect(0, 0, 100, 40), _model.View(client.Id)!.FloatingPanes.Single().Area);
        Assert.Equal((98, 38), (box.Cols, box.Rows));
    }

    [Fact]
    public void The_keyboard_nudges_the_focused_float_from_where_it_is_drawn()
    {
        var (tile, client) = Workspace("techweb");
        var box = _model.SpawnFloat("techweb", "C:/x", ["pwsh"]);
        _model.MoveFloat(box.Id, 500, 8);

        Assert.True(_model.NudgeFloat(client.Id, -1, 1, 0, 0));
        Assert.Equal(new Rect(39, 9, 60, 24), _model.View(client.Id)!.FloatingPanes.Single().Area);

        Assert.True(_model.NudgeFloat(client.Id, 0, 0, -1, 2));
        Assert.Equal(new Rect(39, 9, 59, 26), _model.View(client.Id)!.FloatingPanes.Single().Area);

        _model.Focus(tile.Id);
        Assert.False(_model.NudgeFloat(client.Id, 1, 0, 0, 0));
    }

    [Fact]
    public void A_tile_can_float_and_come_back_beside_the_active_pane()
    {
        var (tile, client) = Workspace("techweb");
        var other = _model.Split(tile.Id, true, false, 50, "C:/x", ["dash"])!;

        Assert.True(_model.ToFloat(other.Id));
        var view = _model.View(client.Id)!;
        Assert.Equal(tile.Id, view.Panes.Single().Pane);
        Assert.Equal(other.Id, view.Focused);

        Assert.True(_model.ToTile(other.Id));
        view = _model.View(client.Id)!;
        Assert.Equal(2, view.Panes.Count);
        Assert.Empty(view.FloatingPanes);
        Assert.Equal(other.Id, view.Focused);
    }

    [Fact]
    public void Floating_the_last_tile_keeps_the_workspace_on_screen()
    {
        var (tile, client) = Workspace("techweb");

        Assert.True(_model.ToFloat(tile.Id));

        var view = _model.View(client.Id)!;
        Assert.Equal("techweb", client.Showing);
        Assert.Null(view.Tab);
        Assert.Equal(tile.Id, view.Focused);

        Assert.True(_model.ToTile(tile.Id));
        Assert.Equal(tile.Id, _model.View(client.Id)!.Panes.Single().Pane);
    }

    [Fact]
    public void A_workspace_goes_away_only_when_its_last_tile_and_float_are_gone()
    {
        var (tile, client) = Workspace("techweb");
        var box = _model.SpawnFloat("techweb", "C:/x", ["pwsh"]);

        _model.Kill(tile.Id);
        Assert.NotNull(_model.Workspace("techweb"));
        Assert.Equal(box.Id, _model.View(client.Id)!.Focused);

        _model.Kill(box.Id);
        Assert.Null(_model.Workspace("techweb"));
        Assert.Null(client.Showing);
    }

    [Fact]
    public void Floats_belong_to_their_workspace_only()
    {
        var (_, laptop) = Workspace("techweb");
        _model.Spawn("fleet", "C:/x", ["claude"]);
        var desktop = _model.Connect(100, 41, "fleet");

        _model.SpawnFloat("techweb", "C:/x", ["pwsh"]);

        Assert.Single(_model.View(laptop.Id)!.FloatingPanes);
        Assert.Empty(_model.View(desktop.Id)!.FloatingPanes);
    }

    [Fact]
    public void A_float_cannot_be_split()
    {
        Workspace("techweb");
        var box = _model.SpawnFloat("techweb", "C:/x", ["pwsh"]);

        Assert.Null(_model.Split(box.Id, true, false, 50, "C:/x", ["dash"]));
    }

    [Fact]
    public void A_float_is_drawn_over_the_tiles_with_its_title_and_owns_the_cursor()
    {
        var (tile, client) = Workspace("techweb", cols: 30, rows: 11);
        var box = _model.SpawnFloat("techweb", "C:/x", ["pwsh"]);
        _model.SetTitle(box.Id, "scratch");
        _model.Resizes();
        var screens = new Dictionary<string, ScreenBuffer>
        {
            [tile.Id] = Filled(tile, 'x'),
            [box.Id] = Filled(box, 'f'),
        };
        screens[box.Id].CursorX = 1;
        screens[box.Id].CursorY = 1;

        var frame = Composer.Compose(_model.View(client.Id)!, id => screens.GetValueOrDefault(id), null);

        var area = _model.View(client.Id)!.FloatingPanes.Single().Area;
        Assert.Equal('╭', frame.At(area.X, area.Y).Text[0]);
        Assert.Contains(" scratch ", frame.RowText(area.Y));
        Assert.Equal('f', frame.At(area.X + 1, area.Y + 1).Text[0]);
        Assert.Equal('x', frame.At(area.X - 1, area.Y + 1).Text[0]);
        Assert.Equal((area.X + 2, area.Y + 2), (frame.CursorX, frame.CursorY));
        Assert.Contains("float 1", frame.RowText(10));
    }

    private static ScreenBuffer Filled(PaneState pane, char c)
    {
        var screen = new ScreenBuffer();
        screen.Resize(pane.Cols, pane.Rows);

        for (var y = 0; y < pane.Rows; y++)
        {
            screen.Write(0, y, new string(c, pane.Cols));
        }

        return screen;
    }
}
