using Fleet.Ui.Constants;
using Fleet.Ui.Models;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Fleet.Ui;

public sealed class FleetActionBar
{
    private readonly ChipStrip _strip;

    public FleetActionBar(Pos y, bool alignRight = false)
    {
        _strip = new ChipStrip
        {
            AlignRight = alignRight,
            X = 1,
            Y = y,
            Width = Dim.Fill(1),
            Height = 1,
            CanFocus = false,
            SchemeName = FleetSchemes.Screen,
        };
    }

    public View Root => _strip;

    public static int Measure(IReadOnlyList<(string Key, string Label, Action Run)> items) =>
        items.Sum(i => (i.Key.Length == 0 ? $" {i.Label} " : $" {i.Key} {i.Label} ").Length + 3) - Math.Min(1, items.Count);

    public void Show(IReadOnlyList<(string Key, string Label, Action Run)> items) =>
        _strip.Show(items);

    public void Pin(string key) => _strip.Pinned = key;

    public static IReadOnlyList<(string Key, string Label, Action Run)> Visible(
        IReadOnlyList<(string Key, string Label, Action Run)> items, bool keys, string? pinned = null) =>
        keys ? items : [.. items.Select(i => i.Key == pinned ? i : (string.Empty, i.Label, i.Run))];

    private sealed class ChipStrip : View
    {
        private readonly List<(int From, int To, Action Run)> _hits = [];

        private IReadOnlyList<(string Key, string Label, Action Run)> _items = [];

        public ChipStrip() => FleetKeyHints.Changed += Rebuild;

        public string? Pinned { get; set; }

        private IReadOnlyList<FleetSpan> _spans = [];

        private int _total;

        public bool AlignRight { get; init; }

        private int Start => AlignRight ? Math.Max(0, Viewport.Width - _total) : 0;

        public void Show(IReadOnlyList<(string Key, string Label, Action Run)> items)
        {
            _items = items;
            Rebuild();
        }

        protected override void Dispose(bool disposing)
        {
            FleetKeyHints.Changed -= Rebuild;
            base.Dispose(disposing);
        }

        private void Rebuild()
        {
            var spans = new List<FleetSpan>();

            _hits.Clear();

            var offset = 0;

            foreach (var (key, label, run) in Visible(_items, FleetKeyHints.Shown, Pinned))
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
            _total = Math.Max(0, offset - 1);

            SetNeedsLayout();
            SetNeedsDraw();
        }

        protected override bool OnDrawingContent(DrawContext? context)
        {
            var basis = GetAttributeForRole(Terminal.Gui.Drawing.VisualRole.Normal);
            var width = Viewport.Width;
            var drawn = Start;

            Move(drawn, 0);

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
                if (at.X - Start >= from && at.X - Start <= to)
                {
                    run();
                    return true;
                }
            }

            return false;
        }
    }
}
