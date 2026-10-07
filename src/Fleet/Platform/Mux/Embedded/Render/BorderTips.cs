using Fleet.Platform.Mux.Embedded.Model;

namespace Fleet.Platform.Mux.Embedded.Render;

public readonly record struct BorderTip(string Pane, string Text, int X, int Y);

public static class BorderTips
{
    public static (string Pane, PlacedButton Placed)? Under(ClientView view, int x, int y)
    {
        if (view.Overlay is not null)
        {
            return null;
        }

        for (var i = view.FloatingPanes.Count - 1; i >= 0; i--)
        {
            var box = view.FloatingPanes[i];

            if (!box.Area.Contains(x, y))
            {
                continue;
            }

            return Hit(box.Pane, box.Area, view.ButtonsOf(box.Pane), x, y);
        }

        foreach (var placed in view.Panes)
        {
            if (Hit(placed.Pane, ClientView.Around(placed.Area), view.FrameButtonsOf(placed.Pane), x, y) is { } hit)
            {
                return hit;
            }
        }

        return null;
    }

    public static BorderTip? At(ClientView view, int x, int y) =>
        Under(view, x, y) is var (pane, placed) ? Place(pane, placed, view.Client.Cols, view.Client.Rows) : null;

    public static BorderTip? Place(string pane, PlacedButton placed, int cols, int rows)
    {
        if (placed.Button.Tip.Length == 0)
        {
            return null;
        }

        var text = $" {placed.Button.Tip} ";
        var width = text.EnumerateRunes().Count();
        var y = placed.Button.Bottom ? placed.Y - 1 : placed.Y + 1;

        if (y < 0 || y >= rows || width > cols)
        {
            return null;
        }

        return new BorderTip(pane, text, Math.Clamp(placed.X, 0, cols - width), y);
    }

    private static (string Pane, PlacedButton Placed)? Hit(string pane, Rect box, IReadOnlyList<FloatButton> buttons, int x, int y)
    {
        foreach (var placed in BorderButtons.Place(box, buttons))
        {
            if (placed.Y == y && x >= placed.X && x < placed.X + placed.Width)
            {
                return (pane, placed);
            }
        }

        return null;
    }
}
