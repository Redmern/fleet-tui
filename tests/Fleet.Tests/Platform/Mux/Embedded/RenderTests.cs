using Fleet.Platform.Mux.Embedded.Model;
using Fleet.Platform.Mux.Embedded.Protocol;
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
        var client = _model.Connect(41, 5, "techweb");
        _model.Resizes();
        Screen(claude, "left");
        Screen(dash, "right");

        var frame = Compose(client.Id);

        Assert.StartsWith("left", frame.RowText(1));
        Assert.Equal('│', frame.At(claude.Cols, 1).Text[0]);
        Assert.Equal("right", frame.RowText(1)[(claude.Cols + 1)..].TrimEnd());
        Assert.Contains("techweb", frame.RowText(0));
        Assert.Contains("1:dashboard", frame.RowText(0));
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
        Assert.Equal(1, frame.CursorY);
        Assert.True(frame.CursorVisible);
    }

    [Fact]
    public void A_badge_is_drawn_at_the_right_of_the_status_bar()
    {
        _model.Spawn("techweb", "C:/x", ["claude"]);
        var client = _model.Connect(30, 4, "techweb");

        var frame = Compose(client.Id, "ctrl+b");

        Assert.Contains(" ctrl+b ", frame.RowText(0));
    }

    private static List<WhichKeyEntry> WhichKeyEntries(int singles)
    {
        var entries = new List<WhichKeyEntry>
        {
            new() { Key = "h j k l", Label = "focus", Group = true },
            new() { Key = "H J K L", Label = "resize", Group = true },
        };

        for (var i = 0; i < singles; i++)
        {
            entries.Add(new WhichKeyEntry { Key = ((char)('a' + i)).ToString(), Label = $"action {i}" });
        }

        return entries;
    }

    private ClientFrame ComposeWhichKey(int cols, int rows, List<WhichKeyEntry> entries)
    {
        _model.Spawn("techweb", "C:/x", ["claude"]);
        var client = _model.Connect(cols, rows, "techweb");
        return Composer.Compose(_model.View(client.Id)!, id => _screens.GetValueOrDefault(id), "ctrl+s", whichKey: entries);
    }

    [Fact]
    public void A_nested_which_key_box_shows_the_breadcrumb_the_back_hint_and_plain_fold_rows()
    {
        List<WhichKeyEntry> entries =
        [
            new() { Key = "h j k l", Label = "focus", Fold = true },
            new() { Key = "t", Label = "show/hide floats" },
        ];
        _model.Spawn("techweb", "C:/x", ["claude"]);
        var client = _model.Connect(80, 24, "techweb");
        var frame = Composer.Compose(
            _model.View(client.Id)!, id => _screens.GetValueOrDefault(id), "ctrl+s › float", whichKey: entries);
        var area = Composer.WhichKeyArea(80, 24, entries);

        Assert.StartsWith("╭─ ctrl+s › float ─", frame.RowText(area.Y)[area.X..]);
        Assert.EndsWith(" esc close · bksp back ─╯", frame.RowText(area.Y + area.Height - 1)[area.X..]);
        Assert.StartsWith("│ h j k l ➜ focus ", frame.RowText(area.Y + 2)[area.X..]);
        Assert.Equal(Composer.Text, frame.At(area.X + frame.RowText(area.Y + 2)[area.X..].IndexOf('➜') + 2, area.Y + 2).Fg);
    }

    [Fact]
    public void The_which_key_box_sits_bottom_right_with_a_rounded_titled_border_padding_and_aligned_rows()
    {
        var entries = WhichKeyEntries(3);
        var frame = ComposeWhichKey(80, 24, entries);
        var area = Composer.WhichKeyArea(80, 24, entries);

        Assert.Equal(80, area.X + area.Width);
        Assert.Equal(24, area.Y + area.Height);
        Assert.InRange(area.Width, 30, 50);
        Assert.Equal(entries.Count + 4, area.Height);

        var top = frame.RowText(area.Y)[area.X..];
        var bottom = frame.RowText(area.Y + area.Height - 1)[area.X..];
        Assert.StartsWith("╭─ ctrl+s ─", top);
        Assert.EndsWith("╮", top);
        Assert.StartsWith("╰", bottom);
        Assert.EndsWith(" esc close ─╯", bottom);
        Assert.Equal("│" + new string(' ', area.Width - 2) + "│", frame.RowText(area.Y + 1)[area.X..]);
        Assert.Equal("│" + new string(' ', area.Width - 2) + "│", frame.RowText(area.Y + area.Height - 2)[area.X..]);

        var rows = Enumerable.Range(area.Y + 2, entries.Count).Select(y => frame.RowText(y)[area.X..]).ToList();
        Assert.StartsWith("│ h j k l ➜ +focus ", rows[0]);
        Assert.StartsWith("│ a       ➜ action 0 ", rows[2]);
        Assert.Single(rows.Select(r => r.IndexOf('➜')).Distinct());

        var separator = area.X + rows[0].IndexOf('➜');
        Assert.Equal(Composer.Flamingo, frame.At(area.X + 2, area.Y + 2).Fg);
        Assert.Equal(Composer.Overlay0, frame.At(separator, area.Y + 2).Fg);
        Assert.Equal(Composer.Blue, frame.At(separator + 2, area.Y + 2).Fg);
        Assert.Equal(Composer.Text, frame.At(separator + 2, area.Y + 4).Fg);
        Assert.Equal(Composer.Blue, frame.At(area.X, area.Y).Fg);
    }

    [Fact]
    public void The_which_key_box_wraps_into_a_second_column_only_when_taller_than_three_quarters_of_the_screen()
    {
        var entries = WhichKeyEntries(6);

        var tall = Composer.WhichKeyArea(80, 40, entries);
        Assert.Equal(entries.Count + 4, tall.Height);

        var frame = ComposeWhichKey(80, 12, entries);
        var area = Composer.WhichKeyArea(80, 12, entries);
        Assert.Equal(80, area.X + area.Width);
        Assert.Equal(12, area.Y + area.Height);
        Assert.True(area.Height <= 12 * 3 / 4, $"height {area.Height}");

        var lines = area.Height - 4;
        var first = frame.RowText(area.Y + 2)[area.X..];
        Assert.Equal(2, first.Count(c => c == '➜'));
        Assert.Contains("+focus", first);
        Assert.Contains($"action {lines - 2}", first);
        var separators = Enumerable.Range(area.Y + 2, lines)
            .Select(y => frame.RowText(y)[area.X..].LastIndexOf('➜'))
            .Distinct();
        Assert.Single(separators);
    }

    [Fact]
    public void The_which_key_box_grows_past_three_quarters_rather_than_drop_entries_when_no_more_columns_fit()
    {
        var entries = WhichKeyEntries(16);
        var frame = ComposeWhichKey(60, 16, entries);
        var area = Composer.WhichKeyArea(60, 16, entries);

        Assert.True(area.Height > 16 * 3 / 4 && area.Y >= 1, $"y {area.Y} height {area.Height}");
        var text = string.Join('\n', Enumerable.Range(area.Y, area.Height).Select(y => frame.RowText(y)));
        Assert.Contains("action 15", text);
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

        Assert.Contains("\e[2;2H", diff);
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

        Assert.StartsWith("fl", next.RowText(1));
        Assert.Contains("fl", FrameEncoder.Encode(shown, next));
        Assert.Equal(version, fleetScreen.Version);
    }
}
