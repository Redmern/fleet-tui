using Fleet.Platform.Mux.Embedded.Native;
using Fleet.Platform.Mux.Embedded.Render;

namespace Fleet.Platform.Mux.Embedded.Daemon;

public sealed class CopySession(string pane, TextPoint cursor)
{
    public string Pane { get; } = pane;

    public TextPoint Cursor { get; private set; } = cursor;

    public TextPoint? Anchor { get; private set; }

    public bool Done { get; private set; }

    public static CopySession Enter(string pane, IPaneTerminal terminal, ScreenBuffer screen)
    {
        var viewport = terminal.Viewport;

        return viewport.AtBottom
            ? new CopySession(pane, new TextPoint(viewport.Top + screen.CursorY, screen.CursorX))
            : new CopySession(pane, new TextPoint(viewport.Top + Math.Max(0, viewport.Rows - 1), 0));
    }

    public string? Apply(string step, IPaneTerminal terminal, int cols)
    {
        var viewport = terminal.Viewport;
        var page = Math.Max(1, viewport.Rows);
        var at = Cursor;

        at = step switch
        {
            "up" => at with { Row = at.Row - 1 },
            "down" => at with { Row = at.Row + 1 },
            "left" => at with { Col = at.Col - 1 },
            "right" => at with { Col = at.Col + 1 },
            "pageup" => at with { Row = at.Row - page },
            "pagedown" => at with { Row = at.Row + page },
            "halfup" => at with { Row = at.Row - page / 2 },
            "halfdown" => at with { Row = at.Row + page / 2 },
            "top" => new TextPoint(0, 0),
            "bottom" => new TextPoint(viewport.Total - 1, 0),
            "start" => at with { Col = 0 },
            "end" => at with { Col = cols - 1 },
            _ => at,
        };

        Cursor = new TextPoint(
            Math.Clamp(at.Row, 0, Math.Max(0, viewport.Total - 1)),
            Math.Clamp(at.Col, 0, Math.Max(0, cols - 1)));

        switch (step)
        {
            case "select":
                Anchor = Anchor is null ? Cursor : null;
                break;
            case "yank":
                {
                    var text = Anchor is { } anchor
                        ? terminal.Text(anchor, Cursor)
                        : terminal.Text(Cursor with { Col = 0 }, Cursor with { Col = cols - 1 });
                    Finish(terminal);
                    return text;
                }

            case "exit":
                Finish(terminal);
                return null;
        }

        Follow(terminal, viewport);
        return null;
    }

    public CopyOverlay Overlay(Viewport viewport) => new(
        Pane,
        Cursor with { Row = Cursor.Row - viewport.Top },
        Anchor is { } anchor ? anchor with { Row = anchor.Row - viewport.Top } : null);

    private void Follow(IPaneTerminal terminal, Viewport viewport)
    {
        if (Cursor.Row < viewport.Top)
        {
            terminal.Scroll(ScrollTo.Row, Cursor.Row);
        }
        else if (Cursor.Row >= viewport.Top + viewport.Rows)
        {
            terminal.Scroll(ScrollTo.Row, Cursor.Row - viewport.Rows + 1);
        }
    }

    private void Finish(IPaneTerminal terminal)
    {
        Done = true;
        terminal.Scroll(ScrollTo.Bottom);
    }
}