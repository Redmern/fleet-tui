using Fleet.Platform.Mux.Embedded.Model;
using Fleet.Platform.Mux.Embedded.Protocol;

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
    public static readonly uint Flamingo = Cell.Rgb(0xf2, 0xcd, 0xcd);
    public static readonly uint Blue = Cell.Rgb(0x89, 0xb4, 0xfa);
    public const char WhichKeySeparator = '➜';
    public const string WhichKeyFooter = "esc close";
    public const string WhichKeyNestedFooter = "esc close · bksp back";
    private const int WhichKeyGap = 3;
    private const int WhichKeyChrome = 2;
    private const int WhichKeyMinWidth = 30;
    private const int WhichKeyMaxWidth = 50;
    public const char LeftCap = '';
    public const char RightCap = '';

    public static ClientFrame Compose(
        ClientView view,
        Func<string, ScreenBuffer?> screens,
        string? badge,
        CopyOverlay? copy = null,
        IReadOnlyList<WhichKeyEntry>? whichKey = null)
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

        if (view.Frame is { } around)
        {
            Framed(frame, around, view.FrameTitle, view.Dividers, focused);
        }

        foreach (var placed in view.Panes)
        {
            Decorate(frame, placed.Pane, placed.Area, screens, copy);
        }

        foreach (var box in view.FloatingPanes)
        {
            var isFocused = box.Pane == view.Focused;
            var inner = ClientView.Inner(box.Area);
            Bordered(frame, box.Area, isFocused ? FocusFg : DividerFg, view.FloatLabel(box.Pane), view.ButtonsOf(box.Pane));
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

        if (badge is not null && whichKey is { Count: > 0 })
        {
            WhichKeyBox(frame, badge, whichKey);
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

    public static Rect WhichKeyArea(int cols, int rows, IReadOnlyList<WhichKeyEntry> entries)
    {
        var (cell, columns, lines) = WhichKeyGrid(cols, rows, entries);
        var natural = columns * cell + (columns - 1) * WhichKeyGap + 2 * WhichKeyChrome;
        var width = Math.Min(cols, columns == 1 ? Math.Clamp(natural, WhichKeyMinWidth, WhichKeyMaxWidth) : natural);
        var height = Math.Min(Math.Max(0, rows - MuxModel.StatusRows), lines + 2 * WhichKeyChrome);
        return new Rect(cols - width, rows - height, width, height);
    }

    private static (int Cell, int Columns, int Lines) WhichKeyGrid(int cols, int rows, IReadOnlyList<WhichKeyEntry> entries)
    {
        var keyWidth = entries.Max(e => e.Key.Length);
        var labelWidth = entries.Max(e => WhichKeyText(e).Length);
        var cell = keyWidth + 3 + labelWidth;
        var tallest = Math.Min(rows - MuxModel.StatusRows, Math.Max(2 * WhichKeyChrome + 1, rows * 3 / 4));
        var fitRows = Math.Max(1, tallest - 2 * WhichKeyChrome);
        var fitColumns = Math.Max(1, (cols - 2 * WhichKeyChrome + WhichKeyGap) / (cell + WhichKeyGap));
        var wanted = (entries.Count + fitRows - 1) / fitRows;
        var columns = Math.Clamp(wanted, 1, fitColumns);
        var maxRows = columns < wanted ? Math.Max(1, rows - MuxModel.StatusRows - 2 * WhichKeyChrome) : fitRows;
        var lines = Math.Min(maxRows, (entries.Count + columns - 1) / columns);
        return (cell, columns, lines);
    }

    private static void WhichKeyBox(ClientFrame frame, string title, IReadOnlyList<WhichKeyEntry> entries)
    {
        var (cell, _, lines) = WhichKeyGrid(frame.Cols, frame.Rows, entries);
        var keyWidth = entries.Max(e => e.Key.Length);
        var area = WhichKeyArea(frame.Cols, frame.Rows, entries);
        var content = new Rect(
            area.X + WhichKeyChrome,
            area.Y + WhichKeyChrome,
            Math.Max(0, area.Width - 2 * WhichKeyChrome),
            Math.Max(0, area.Height - 2 * WhichKeyChrome));

        var nested = title.Contains(BadgeMessage.Breadcrumb, StringComparison.Ordinal);
        Box(frame, area, Blue, title, nested ? WhichKeyNestedFooter : WhichKeyFooter);
        Clear(frame, ClientView.Inner(area));

        for (var i = 0; i < entries.Count; i++)
        {
            var row = i % lines;
            var x = content.X + i / lines * (cell + WhichKeyGap);
            var y = content.Y + row;
            if (row >= content.Height || x >= content.X + content.Width)
            {
                continue;
            }

            var entry = entries[i];
            Write(frame, x, y, entry.Key.PadRight(keyWidth), Flamingo, CellAttr.None, content);
            Write(frame, x + keyWidth + 1, y, WhichKeySeparator.ToString(), Overlay0, CellAttr.None, content);
            Write(
                frame,
                x + keyWidth + 3,
                y,
                WhichKeyText(entry),
                entry.Group ? Blue : Text,
                CellAttr.None,
                content);
        }
    }

    private static string WhichKeyText(WhichKeyEntry entry) =>
        (entry.Icon is { Length: 1 } icon && !char.IsSurrogate(icon[0]) ? icon + " " : string.Empty)
        + (entry.Group ? "+" : string.Empty)
        + entry.Label;

    private static void Write(ClientFrame frame, int x, int y, string text, uint fg, CellAttr attrs, Rect clip)
    {
        for (var i = 0; i < text.Length && x + i < clip.X + clip.Width; i++)
        {
            frame.Cells[y * frame.Cols + x + i] = Cell.Of(text[i], fg, Cell.Default, attrs);
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

    private static void Box(ClientFrame frame, Rect area, uint fg, string? title, string? footer = null)
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

        var lastRow = area.Y + area.Height - 1;
        if (footer is { Length: > 0 } && area.Height > 1 && lastRow < frame.Rows && footer.Length + 2 <= area.Width - 4)
        {
            var label = $" {footer} ";
            var start = area.X + area.Width - 2 - label.Length;

            for (var i = 0; i < label.Length && start + i < frame.Cols; i++)
            {
                frame.Cells[lastRow * frame.Cols + start + i] = Cell.Of(label[i], fg);
            }
        }
    }

    private static void Bordered(ClientFrame frame, Rect area, uint fg, string? title, IReadOnlyList<FloatButton> buttons)
    {
        if (buttons.Count == 0)
        {
            Box(frame, area, fg, title);
            return;
        }

        Box(frame, area, fg, null);

        var placed = BorderButtons.Place(area, buttons);
        var (from, to) = BorderButtons.TitleRoom(area, placed);

        if (title is { Length: > 0 } && area.Y < frame.Rows && to - from >= 3)
        {
            var label = $" {title} ";
            label = label.Length > to - from ? label[..(to - from)] : label;

            for (var i = 0; i < label.Length && from + i < frame.Cols; i++)
            {
                frame.Cells[area.Y * frame.Cols + from + i] = Cell.Of(label[i], fg);
            }
        }

        foreach (var button in placed)
        {
            Pill(frame, button);
        }
    }

    private static void Pill(ClientFrame frame, PlacedButton placed)
    {
        if (placed.Y < 0 || placed.Y >= frame.Rows)
        {
            return;
        }

        var x = placed.X;

        void Put(string text, uint fg, uint bg)
        {
            foreach (var rune in text.EnumerateRunes())
            {
                if (x >= 0 && x < frame.Cols)
                {
                    frame.Cells[placed.Y * frame.Cols + x] = new Cell(rune.ToString(), fg, bg, CellAttr.None, 0, false);
                }

                x++;
            }
        }

        var button = placed.Button;
        Put(LeftCap.ToString(), Surface0, Cell.Default);

        if (button.Key.Length > 0)
        {
            Put($" {button.Key} ", Blue, Surface0);
            Put($"{button.Label} ", Text, Surface0);
        }
        else
        {
            Put($" {button.Label} ", Text, Surface0);
        }

        Put(RightCap.ToString(), Surface0, Cell.Default);
    }

    private static void Framed(ClientFrame frame, Rect box, string? title, IReadOnlyList<Divider> dividers, Rect focused)
    {
        var right = box.X + box.Width - 1;
        var bottom = box.Y + box.Height - 1;

        bool Near(int x, int y) =>
            focused.Width > 0
            && x >= focused.X - 1 && x <= focused.X + focused.Width
            && y >= focused.Y - 1 && y <= focused.Y + focused.Height;

        void Put(int x, int y, char c)
        {
            if (x >= 0 && y >= 0 && x < frame.Cols && y < frame.Rows)
            {
                frame.Cells[y * frame.Cols + x] = Cell.Of(c, Near(x, y) ? FocusFg : DividerFg);
            }
        }

        for (var x = box.X; x <= right; x++)
        {
            Put(x, box.Y, x == box.X ? '╭' : x == right ? '╮' : '─');
            Put(x, bottom, x == box.X ? '╰' : x == right ? '╯' : '─');
        }

        for (var y = box.Y + 1; y < bottom; y++)
        {
            Put(box.X, y, '│');
            Put(right, y, '│');
        }

        foreach (var divider in dividers)
        {
            if (divider.Vertical)
            {
                if (divider.Y == box.Y + 1)
                {
                    Put(divider.X, box.Y, '┬');
                }

                if (divider.Y + divider.Length == bottom)
                {
                    Put(divider.X, bottom, '┴');
                }
            }
            else
            {
                if (divider.X == box.X + 1)
                {
                    Put(box.X, divider.Y, '├');
                }

                if (divider.X + divider.Length == right)
                {
                    Put(right, divider.Y, '┤');
                }
            }
        }

        if (title is { Length: > 0 } && box.Width > 6)
        {
            var label = $" {title} ";
            label = label.Length > box.Width - 4 ? label[..(box.Width - 4)] : label;

            for (var i = 0; i < label.Length; i++)
            {
                Put(box.X + 2 + i, box.Y, label[i]);
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

    public static (int Start, int End)? NoticeSpan(ClientView view) =>
        Bar(view).Where(s => s.Segment.Notices).Select(s => (s.Start, s.Start + s.Segment.Text.Length))
            .Cast<(int, int)?>()
            .FirstOrDefault();

    public static IReadOnlyList<(BarSegment Segment, int Start)> Bar(ClientView view)
    {
        var parts = new List<BarSegment> { new(" ", Cell.Default, Cell.Default) };

        if (view.Workspace is not { } workspace)
        {
            parts.Add(new BarSegment(" nothing to show ", Overlay0, Cell.Default));
            return Place(parts);
        }

        var (here, elsewhere) = view.Notices;
        var noticed = here > 0 || elsewhere > 0;
        var counts = !noticed
            ? string.Empty
            : $" ●{(here > 0 ? $" {here}" : string.Empty)}{(elsewhere > 0 ? $" +{elsewhere}" : string.Empty)}";

        parts.Add(new BarSegment(
            $"{LeftCap} {workspace.Name}{view.Client.Label}{counts} {RightCap}", Crust, Lavender, CellAttr.Bold, Notices: noticed, Caps: true));

        for (var i = 0; i < workspace.Tabs.Count; i++)
        {
            var tab = workspace.Tabs[i];
            var label = $"{i + 1}:{(tab.Title.Length > 0 ? tab.Title : "shell")}{(tab.Zoomed is not null ? " Z" : string.Empty)}";
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

        if (view.Workspace?.RemoteHost is null)
        {
            Put(0, new string(' ', frame.Cols), Cell.Default, Cell.Default, CellAttr.None, false);

            foreach (var (segment, start) in Bar(view))
            {
                Put(start, segment.Text, segment.Fg, segment.Bg, segment.Attrs, segment.Caps);
            }
        }

        if (badge is { Length: > 0 })
        {
            var text = $"{LeftCap} {badge} {RightCap}";
            Put(Math.Max(0, frame.Cols - text.Length - 1), text, Crust, Yellow, CellAttr.Bold, true);
        }
    }
}

public sealed record BarSegment(
    string Text, uint Fg, uint Bg, CellAttr Attrs = CellAttr.None, string? Tab = null, bool Floats = false, bool Caps = false,
    bool Notices = false);