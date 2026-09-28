using Fleet.Platform.Mux.Embedded.Model;

namespace Fleet.Platform.Mux.Embedded.Render;

public static class Composer
{
    public static readonly uint DividerFg = Cell.Palette(8);
    public static readonly uint FocusFg = Cell.Palette(4);
    public static readonly uint BarBg = Cell.Palette(236);
    public static readonly uint BarFg = Cell.Palette(250);
    public static readonly uint ActiveBg = Cell.Palette(25);
    public static readonly uint ActiveFg = Cell.Palette(15);
    public static readonly uint BadgeBg = Cell.Palette(11);
    public static readonly uint BadgeFg = Cell.Palette(0);
    public static readonly uint Crust = Cell.Rgb(0x11, 0x11, 0x1b);
    public static readonly uint Lavender = Cell.Rgb(0xb4, 0xbe, 0xfe);
    public static readonly uint Overlay0 = Cell.Rgb(0x6c, 0x70, 0x86);
    public static readonly uint Text = Cell.Rgb(0xcd, 0xd6, 0xf4);
    public static readonly uint Surface0 = Cell.Rgb(0x31, 0x32, 0x44);
    public static readonly uint Yellow = Cell.Rgb(0xf9, 0xe2, 0xaf);
    public const char LeftCap = '';
    public const char RightCap = '';

    public static ClientFrame Compose(
        ClientView view, Func<string, ScreenBuffer?> screens, string? badge, CopyOverlay? copy = null)
    {
        var frame = new ClientFrame(view.Client.Cols, view.Client.Rows);

        foreach (var placed in view.Panes)
        {
            if (screens(placed.Pane) is { } screen)
            {
                Blit(frame, screen, placed.Area);

                if (placed.Pane == view.Focused)
                {
                    frame.CursorX = placed.Area.X + Math.Min(screen.CursorX, Math.Max(0, placed.Area.Width - 1));
                    frame.CursorY = placed.Area.Y + Math.Min(screen.CursorY, Math.Max(0, placed.Area.Height - 1));
                    frame.CursorVisible = screen.CursorVisible
                        && screen.CursorX < placed.Area.Width
                        && screen.CursorY < placed.Area.Height;
                    frame.CursorShape = screen.CursorShape;
                }
            }
        }

        var focused = view.Panes.FirstOrDefault(p => p.Pane == view.Focused).Area;

        foreach (var divider in view.Dividers)
        {
            Draw(frame, divider, focused);
        }

        foreach (var placed in view.Panes)
        {
            Decorate(frame, placed.Pane, placed.Area, screens, copy);
        }

        foreach (var box in view.FloatingPanes)
        {
            var isFocused = box.Pane == view.Focused;
            var inner = ClientView.Inner(box.Area);
            Box(frame, box.Area, isFocused ? FocusFg : DividerFg, view.FloatLabel(box.Pane));
            Clear(frame, inner);

            if (screens(box.Pane) is { } screen)
            {
                Blit(frame, screen, inner);

                if (isFocused)
                {
                    frame.CursorX = inner.X + Math.Min(screen.CursorX, Math.Max(0, inner.Width - 1));
                    frame.CursorY = inner.Y + Math.Min(screen.CursorY, Math.Max(0, inner.Height - 1));
                    frame.CursorVisible = screen.CursorVisible
                        && screen.CursorX < inner.Width
                        && screen.CursorY < inner.Height;
                    frame.CursorShape = screen.CursorShape;
                }
            }

            Decorate(frame, box.Pane, inner, screens, copy);
        }

        if (view.Overlay is { } overlay)
        {
            Box(frame, overlay.Area, FocusFg, null);
            var inner = new Rect(overlay.Area.X + 1, overlay.Area.Y + 1, Math.Max(0, overlay.Area.Width - 2), Math.Max(0, overlay.Area.Height - 2));

            if (screens(overlay.Pane) is { } screen)
            {
                Blit(frame, screen, inner);
                frame.CursorX = inner.X + Math.Min(screen.CursorX, Math.Max(0, inner.Width - 1));
                frame.CursorY = inner.Y + Math.Min(screen.CursorY, Math.Max(0, inner.Height - 1));
                frame.CursorVisible = screen.CursorVisible;
                frame.CursorShape = screen.CursorShape;
            }
        }

        StatusBar(frame, view, badge);
        return frame;
    }

    private static void Blit(ClientFrame frame, ScreenBuffer screen, Rect area)
    {
        var rows = Math.Min(area.Height, screen.Rows);
        var cols = Math.Min(area.Width, screen.Cols);

        for (var y = 0; y < rows && area.Y + y < frame.Rows; y++)
        {
            var source = screen.Row(y);
            var target = frame.Cells.AsSpan((area.Y + y) * frame.Cols + area.X, Math.Min(area.Width, frame.Cols - area.X));

            for (var x = 0; x < cols && x < target.Length; x++)
            {
                var cell = source[x];
                if (cell.Wide && x + 1 >= cols)
                {
                    cell = cell with { Text = " ", Wide = false };
                }

                target[x] = cell;
            }
        }
    }

    private static void Decorate(
        ClientFrame frame, string pane, Rect area, Func<string, ScreenBuffer?> screens, CopyOverlay? copy)
    {
        if (screens(pane) is { Viewport.AtBottom: false } scrolled)
        {
            Marker(frame, area, $"[{scrolled.Viewport.Below}/{scrolled.Viewport.History}]");
        }

        if (copy is not null && copy.Pane == pane)
        {
            Copying(frame, area, copy);
        }
    }

    private static void Marker(ClientFrame frame, Rect area, string text)
    {
        if (area.Height <= 0 || text.Length > area.Width || area.Y >= frame.Rows)
        {
            return;
        }

        var x = area.X + area.Width - text.Length;
        for (var i = 0; i < text.Length && x + i < frame.Cols; i++)
        {
            frame.Cells[area.Y * frame.Cols + x + i] = Cell.Of(text[i], BadgeFg, BadgeBg, CellAttr.Bold);
        }
    }

    private static void Copying(ClientFrame frame, Rect area, CopyOverlay copy)
    {
        if (copy.Anchor is { } anchor)
        {
            var (from, to) = anchor.CompareTo(copy.Cursor) <= 0 ? (anchor, copy.Cursor) : (copy.Cursor, anchor);

            for (var y = 0; y < area.Height && area.Y + y < frame.Rows; y++)
            {
                for (var x = 0; x < area.Width && area.X + x < frame.Cols; x++)
                {
                    var here = new TextPoint(y, x);
                    if (here.CompareTo(from) >= 0 && here.CompareTo(to) <= 0)
                    {
                        ref var cell = ref frame.Cells[(area.Y + y) * frame.Cols + area.X + x];
                        cell = cell with { Attrs = cell.Attrs ^ CellAttr.Inverse };
                    }
                }
            }
        }

        var visible = copy.Cursor.Row >= 0 && copy.Cursor.Row < area.Height;
        frame.CursorVisible = visible;
        frame.CursorShape = 2;

        if (visible)
        {
            frame.CursorX = area.X + Math.Min(copy.Cursor.Col, Math.Max(0, area.Width - 1));
            frame.CursorY = area.Y + (int)copy.Cursor.Row;
        }
    }

    private static void Clear(ClientFrame frame, Rect area)
    {
        for (var y = area.Y; y < area.Y + area.Height && y < frame.Rows; y++)
        {
            for (var x = area.X; x < area.X + area.Width && x < frame.Cols; x++)
            {
                frame.Cells[y * frame.Cols + x] = Cell.Blank;
            }
        }
    }

    private static void Box(ClientFrame frame, Rect area, uint fg, string? title)
    {
        for (var y = area.Y; y < area.Y + area.Height && y < frame.Rows; y++)
        {
            for (var x = area.X; x < area.X + area.Width && x < frame.Cols; x++)
            {
                var top = y == area.Y;
                var bottom = y == area.Y + area.Height - 1;
                var left = x == area.X;
                var right = x == area.X + area.Width - 1;

                var c = (top, bottom, left, right) switch
                {
                    (true, _, true, _) => '╭',
                    (true, _, _, true) => '╮',
                    (_, true, true, _) => '╰',
                    (_, true, _, true) => '╯',
                    (true, _, _, _) or (_, true, _, _) => '─',
                    (_, _, true, _) or (_, _, _, true) => '│',
                    _ => ' ',
                };

                frame.Cells[y * frame.Cols + x] = Cell.Of(c, fg);
            }
        }

        if (title is { Length: > 0 } && area.Width > 6 && area.Y < frame.Rows)
        {
            var label = $" {title} ";
            label = label.Length > area.Width - 4 ? label[..(area.Width - 4)] : label;

            for (var i = 0; i < label.Length && area.X + 2 + i < frame.Cols; i++)
            {
                frame.Cells[area.Y * frame.Cols + area.X + 2 + i] = Cell.Of(label[i], fg);
            }
        }
    }

    private static void Draw(ClientFrame frame, Divider divider, Rect focused)
    {
        for (var i = 0; i < divider.Length; i++)
        {
            var x = divider.Vertical ? divider.X : divider.X + i;
            var y = divider.Vertical ? divider.Y + i : divider.Y;

            if (x < 0 || y < 0 || x >= frame.Cols || y >= frame.Rows)
            {
                continue;
            }

            var touchesFocus = divider.Vertical
                ? y >= focused.Y && y < focused.Y + focused.Height
                  && (x == focused.X - 1 || x == focused.X + focused.Width)
                : x >= focused.X && x < focused.X + focused.Width
                  && (y == focused.Y - 1 || y == focused.Y + focused.Height);

            frame.Cells[y * frame.Cols + x] = Cell.Of(
                divider.Vertical ? '│' : '─', touchesFocus ? FocusFg : DividerFg);
        }
    }

    public static IReadOnlyList<(string Tab, int Start, int End, string Label)> TabSpans(ClientView view) =>
        Bar(view)
            .Where(s => s.Segment.Tab is not null)
            .Select(s => (s.Segment.Tab!, s.Start, s.Start + s.Segment.Text.Length, s.Segment.Text))
            .ToList();

    public static (int Start, int End, string Label)? FloatSpan(ClientView view) =>
        Bar(view).Where(s => s.Segment.Floats).Select(s => (s.Start, s.Start + s.Segment.Text.Length, s.Segment.Text))
            .Cast<(int, int, string)?>()
            .FirstOrDefault();

    public static IReadOnlyList<(BarSegment Segment, int Start)> Bar(ClientView view)
    {
        var parts = new List<BarSegment> { new(" ", Cell.Default, Cell.Default) };

        if (view.Workspace is not { } workspace)
        {
            parts.Add(new BarSegment(" nothing to show ", Overlay0, Cell.Default));
            return Place(parts);
        }

        parts.AddRange(Pill($" {workspace.Name} ", Lavender, Crust, CellAttr.Bold));

        for (var i = 0; i < workspace.Tabs.Count; i++)
        {
            var tab = workspace.Tabs[i];
            var label = $"{i + 1}:{(tab.Title.Length > 0 ? tab.Title : "shell")}";
            parts.Add(new BarSegment(" ", Cell.Default, Cell.Default));

            if (tab.Id == workspace.ActiveTab)
            {
                parts.Add(new BarSegment($"{LeftCap} {label} {RightCap}", Crust, Lavender, CellAttr.Bold, tab.Id, Caps: true));
            }
            else
            {
                parts.Add(new BarSegment($" {label} ", Overlay0, Cell.Default, CellAttr.None, tab.Id));
            }
        }

        var floats = workspace.Floats.Count(f => !f.Modal);
        if (floats > 0)
        {
            parts.Add(new BarSegment("  ", Cell.Default, Cell.Default));
            parts.Add(workspace.FloatsShown
                ? new BarSegment($"{LeftCap} float {floats} {RightCap}", Text, Surface0, CellAttr.None, Floats: true, Caps: true)
                : new BarSegment($" float {floats} ", Overlay0, Cell.Default, CellAttr.None, Floats: true));
        }

        return Place(parts);
    }

    private static IEnumerable<BarSegment> Pill(string text, uint accent, uint fg, CellAttr attrs) =>
        [new BarSegment($"{LeftCap}{text}{RightCap}", fg, accent, attrs, Caps: true)];

    private static List<(BarSegment Segment, int Start)> Place(List<BarSegment> parts)
    {
        var placed = new List<(BarSegment, int)>();
        var x = 0;

        foreach (var part in parts)
        {
            placed.Add((part, x));
            x += part.Text.Length;
        }

        return placed;
    }

    private static void StatusBar(ClientFrame frame, ClientView view, string? badge)
    {
        const int y = 0;

        void Put(int x, string text, uint fg, uint bg, CellAttr attrs, bool caps)
        {
            for (var i = 0; i < text.Length && x + i < frame.Cols; i++)
            {
                var cap = caps && (i == 0 || i == text.Length - 1);
                frame.Cells[y * frame.Cols + x + i] = cap
                    ? Cell.Of(text[i], bg, Cell.Default)
                    : Cell.Of(text[i], fg, bg, attrs);
            }
        }

        Put(0, new string(' ', frame.Cols), Cell.Default, Cell.Default, CellAttr.None, false);

        foreach (var (segment, start) in Bar(view))
        {
            Put(start, segment.Text, segment.Fg, segment.Bg, segment.Attrs, segment.Caps);
        }

        if (badge is { Length: > 0 })
        {
            var text = $"{LeftCap} {badge} {RightCap}";
            Put(Math.Max(0, frame.Cols - text.Length - 1), text, Crust, Yellow, CellAttr.Bold, true);
        }
    }
}

public sealed record BarSegment(
    string Text, uint Fg, uint Bg, CellAttr Attrs = CellAttr.None, string? Tab = null, bool Floats = false, bool Caps = false);