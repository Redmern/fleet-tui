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

    public static ClientFrame Compose(ClientView view, Func<string, ScreenBuffer?> screens, string? badge)
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

        if (view.Overlay is { } overlay)
        {
            Box(frame, overlay.Area);
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

        for (var y = 0; y < rows && area.Y + y < frame.Rows - MuxModel.StatusRows; y++)
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

    private static void Box(ClientFrame frame, Rect area)
    {
        for (var y = area.Y; y < area.Y + area.Height && y < frame.Rows - MuxModel.StatusRows; y++)
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

                frame.Cells[y * frame.Cols + x] = Cell.Of(c, FocusFg);
            }
        }
    }

    private static void Draw(ClientFrame frame, Divider divider, Rect focused)
    {
        for (var i = 0; i < divider.Length; i++)
        {
            var x = divider.Vertical ? divider.X : divider.X + i;
            var y = divider.Vertical ? divider.Y + i : divider.Y;

            if (x < 0 || y < 0 || x >= frame.Cols || y >= frame.Rows - MuxModel.StatusRows)
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

    public static IReadOnlyList<(string Tab, int Start, int End, string Label)> TabSpans(ClientView view)
    {
        var spans = new List<(string, int, int, string)>();

        if (view.Workspace is null)
        {
            return spans;
        }

        var x = view.Workspace.Name.Length + 2;

        for (var i = 0; i < view.Workspace.Tabs.Count; i++)
        {
            var tab = view.Workspace.Tabs[i];
            var label = $" {i + 1}:{(tab.Title.Length > 0 ? tab.Title : "shell")} ";
            spans.Add((tab.Id, x, x + label.Length, label));
            x += label.Length;
        }

        return spans;
    }

    private static void StatusBar(ClientFrame frame, ClientView view, string? badge)
    {
        var y = frame.Rows - 1;
        var x = 0;

        void Put(string text, uint fg, uint bg, CellAttr attrs = CellAttr.None)
        {
            foreach (var c in text)
            {
                if (x >= frame.Cols)
                {
                    return;
                }

                frame.Cells[y * frame.Cols + x++] = Cell.Of(c, fg, bg, attrs);
            }
        }

        Put(new string(' ', frame.Cols), BarFg, BarBg);
        x = 0;

        if (view.Workspace is null)
        {
            Put(" nothing to show ", BarFg, BarBg);
        }
        else
        {
            Put($" {view.Workspace.Name} ", ActiveFg, BarBg, CellAttr.Bold);

            foreach (var (tab, _, _, label) in TabSpans(view))
            {
                var active = tab == view.Workspace.ActiveTab;
                Put(label, active ? ActiveFg : BarFg, active ? ActiveBg : BarBg);
            }
        }

        if (badge is { Length: > 0 })
        {
            x = Math.Max(0, frame.Cols - badge.Length - 2);
            Put($" {badge} ", BadgeFg, BadgeBg, CellAttr.Bold);
        }
    }
}
