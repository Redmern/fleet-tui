using Fleet.Ui.Constants;
using Fleet.Ui.Models;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;

namespace Fleet.Ui;

public sealed class FleetCanvas : View
{
    private IReadOnlyList<IReadOnlyList<FleetSpan>> _lines = [];

    private (int Line, int From, int To) _highlight = (-1, 0, 0);

    private int _top;

    public FleetCanvas(Pos x, Pos y, Dim height)
    {
        X = x;
        Y = y;
        Width = Dim.Fill(1);
        Height = height;
        CanFocus = true;
        SchemeName = FleetSchemes.Screen;
    }

    public void Show(IReadOnlyList<IReadOnlyList<FleetSpan>> lines, (int Line, int From, int To) highlight)
    {
        _lines = lines;
        _highlight = highlight;

        Reveal();
        SetNeedsDraw();
    }

    private void Reveal()
    {
        var rows = Viewport.Height;

        if (rows <= 0 || _highlight.Line < 0)
        {
            _top = 0;
            return;
        }

        var above = Math.Max(0, _highlight.Line - 1);
        var below = Math.Min(_lines.Count - 1, _highlight.Line + 1);

        _top = Math.Min(_top, above);
        _top = Math.Max(_top, below - rows + 1);
        _top = Math.Clamp(_top, 0, Math.Max(0, _lines.Count - rows));
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        Reveal();

        var normal = GetAttributeForRole(VisualRole.Normal);
        var focus = GetAttributeForRole(VisualRole.Focus);
        var width = Viewport.Width;

        for (var row = 0; row < Viewport.Height; row++)
        {
            var line = _top + row;
            var drawn = 0;

            Move(0, row);

            if (line < _lines.Count)
            {
                foreach (var span in _lines[line])
                {
                    foreach (var rune in span.Text.EnumerateRunes())
                    {
                        if (drawn >= width)
                        {
                            break;
                        }

                        var lit = line == _highlight.Line && drawn >= _highlight.From && drawn < _highlight.To;

                        SetAttribute(FleetInk.For(span.Tone, lit ? focus : normal));
                        AddRune(rune);
                        drawn++;
                    }
                }
            }

            SetAttribute(normal);

            while (drawn < width)
            {
                AddRune((System.Text.Rune)' ');
                drawn++;
            }
        }

        return true;
    }
}
