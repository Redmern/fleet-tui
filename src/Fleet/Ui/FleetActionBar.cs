using Fleet.Ui.Constants;
using Fleet.Ui.Models;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Fleet.Ui;

public sealed class FleetActionBar
{
    private readonly ChipStrip _strip;

    public FleetActionBar(Pos y)
    {
        _strip = new ChipStrip
        {
            X = 1,
            Y = y,
            Width = Dim.Fill(1),
            Height = 1,
            CanFocus = false,
            SchemeName = FleetSchemes.Screen,
        };
    }

    public View Root => _strip;

    public void Show(IReadOnlyList<(string Key, string Label, Action Run)> items) =>
        _strip.Show(items);

    private sealed class ChipStrip : View
    {
        private readonly List<(int From, int To, Action Run)> _hits = [];

        private IReadOnlyList<FleetSpan> _spans = [];

        public void Show(IReadOnlyList<(string Key, string Label, Action Run)> items)
        {
            var spans = new List<FleetSpan>();

            _hits.Clear();

            var offset = 0;

            foreach (var (key, label, run) in items)
            {
                var text = key.Length == 0 ? $" {label} " : $" {key} {label} ";
                var chip = text.Length + 2;

                spans.Add(new FleetSpan(FleetGlyphs.PillLeft, FleetTones.ChipEdge));

                if (key.Length > 0)
                {
                    spans.Add(new FleetSpan($" {key} ", FleetTones.ChipKey));
                    spans.Add(new FleetSpan($"{label} ", FleetTones.ChipLabel));
                }
                else
                {
                    spans.Add(new FleetSpan($" {label} ", FleetTones.ChipLabel));
                }

                spans.Add(new FleetSpan(FleetGlyphs.PillRight, FleetTones.ChipEdge));
                spans.Add(FleetSpan.Plain(" "));

                _hits.Add((offset, offset + chip - 1, run));

                offset += chip + 1;
            }

            _spans = spans;

            SetNeedsLayout();
            SetNeedsDraw();
        }

        protected override bool OnDrawingContent(DrawContext? context)
        {
            var basis = GetAttributeForRole(Terminal.Gui.Drawing.VisualRole.Normal);
            var width = Viewport.Width;
            var drawn = 0;

            Move(0, 0);

            foreach (var span in _spans)
            {
                SetAttribute(FleetInk.For(span.Tone, basis));

                foreach (var rune in span.Text.EnumerateRunes())
                {
                    if (drawn >= width)
                    {
                        return true;
                    }

                    AddRune(rune);
                    drawn++;
                }
            }

            return true;
        }

        protected override bool OnMouseEvent(Mouse mouse)
        {
            if (!mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked))
            {
                return false;
            }

            if (mouse.Position is not { } at)
            {
                return false;
            }

            foreach (var (from, to, run) in _hits)
            {
                if (at.X >= from && at.X <= to)
                {
                    run();
                    return true;
                }
            }

            return false;
        }
    }
}
