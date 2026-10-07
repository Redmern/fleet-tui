using System.Drawing;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Settings.Enums;
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
            Visible = !FloatBorder.Enabled,
            SchemeName = FleetSchemes.Screen,
        };
        FloatBorder.Track(this);
    }

    public View Root => _strip;

    public IReadOnlyList<(string Key, string Label, Action Run)> Items => _strip.Items;

    public bool AlignRight => _strip.AlignRight;

    public string? Pinned => _strip.Pinned;

    public static int Measure(IReadOnlyList<(string Key, string Label, Action Run)> items) =>
        Measure(items, FleetButtonHints.Mode);

    public static int Measure(IReadOnlyList<(string Key, string Label, Action Run)> items, ButtonHints hints) =>
        Width(Chips(items, true, null, hints));

    public static int Width(IReadOnlyList<FleetChip> chips) => chips.Count == 0 ? 0 : chips[^1].To + 1;

    public static IReadOnlyList<FleetChip> Chips(
        IReadOnlyList<(string Key, string Label, Action Run)> items,
        bool keys,
        string? pinned,
        ButtonHints hints,
        IReadOnlyList<string>? tips = null)
    {
        var chips = new List<FleetChip>();
        var offset = 0;

        var visible = Visible(items, keys, pinned);

        for (var i = 0; i < visible.Count; i++)
        {
            var (key, label, run) = visible[i];
            var face = FleetButtonHints.Face(label, hints);
            var spans = new List<FleetSpan> { new(FleetGlyphs.PillLeft, FleetTones.ChipEdge) };

            if (key.Length > 0)
            {
                spans.Add(new FleetSpan($" {key} ", FleetTones.ChipKey));
                spans.Add(new FleetSpan($"{face} ", FleetTones.ChipLabel));
            }
            else
            {
                spans.Add(new FleetSpan($" {face} ", FleetTones.ChipLabel));
            }

            spans.Add(new FleetSpan(FleetGlyphs.PillRight, FleetTones.ChipEdge));
            spans.Add(FleetSpan.Plain(" "));

            var width = spans.Sum(s => s.Text.Length) - 1;

            chips.Add(new FleetChip(offset, offset + width - 1, FleetIcons.Name(label), run, spans, tips is { } bound && i < bound.Count ? bound[i] : string.Empty));

            offset += width + 1;
        }

        return chips;
    }

    public static int Hovered(IReadOnlyList<FleetChip> chips, int x)
    {
        for (var i = 0; i < chips.Count; i++)
        {
            if (x >= chips[i].From && x <= chips[i].To)
            {
                return i;
            }
        }

        return -1;
    }

    public void Show(IReadOnlyList<(string Key, string Label, Action Run)> items) => Show(items, []);

    public void Show(IReadOnlyList<(string Key, string Label, Action Run, FleetAction Action)> items) =>
        Show(
            [.. items.Select(i => (i.Key, i.Label, i.Run))],
            [.. items.Select(i => i.Action == FleetAction.None ? string.Empty : i.Key)]);

    private void Show(IReadOnlyList<(string Key, string Label, Action Run)> items, IReadOnlyList<string> tips)
    {
        _strip.Show(items, tips);
        FloatBorder.Refresh();
    }

    public void Pin(string key)
    {
        _strip.Pinned = key;
        FloatBorder.Refresh();
    }

    public static IReadOnlyList<(string Key, string Label, Action Run)> Visible(
        IReadOnlyList<(string Key, string Label, Action Run)> items, bool keys, string? pinned = null) =>
        keys ? items : [.. items.Select(i => i.Key == pinned ? i : (string.Empty, i.Label, i.Run))];

    private sealed class ChipStrip : View
    {
        private IReadOnlyList<(string Key, string Label, Action Run)> _items = [];

        private IReadOnlyList<string> _tips = [];

        private IReadOnlyList<FleetChip> _chips = [];

        private int _hovered = -1;

        public ChipStrip()
        {
            MousePositionTracking = true;
            FleetKeyHints.Changed += Rebuild;
            FleetButtonHints.Changed += Rebuild;
            MouseLeave += (_, _) => Hover(-1);
        }

        public string? Pinned { get; set; }

        public IReadOnlyList<(string Key, string Label, Action Run)> Items => _items;

        public bool AlignRight { get; init; }

        private int Start => AlignRight ? Math.Max(0, Viewport.Width - FleetActionBar.Width(_chips)) : 0;

        public void Show(IReadOnlyList<(string Key, string Label, Action Run)> items, IReadOnlyList<string> tips)
        {
            _items = items;
            _tips = tips;
            Rebuild();
        }

        protected override void Dispose(bool disposing)
        {
            FleetKeyHints.Changed -= Rebuild;
            FleetButtonHints.Changed -= Rebuild;
            FloatBorder.Forget(this);
            Hover(-1);
            base.Dispose(disposing);
        }

        private void Rebuild()
        {
            _chips = Chips(_items, FleetKeyHints.Shown, Pinned, FleetButtonHints.Mode, _tips);

            Hover(-1);
            SetNeedsLayout();
            SetNeedsDraw();
        }

        private void Hover(int index)
        {
            if (index == _hovered)
            {
                return;
            }

            _hovered = index;

            if (index < 0 || index >= _chips.Count)
            {
                FleetToolTip.Hide();
                return;
            }

            var at = ViewportToScreen(new Rectangle(Start + _chips[index].From, 0, 1, 1)).Location;

            FleetToolTip.Show(this, new Point(at.X, at.Y + 1), _chips[index].Tip);
        }

        protected override bool OnDrawingContent(DrawContext? context)
        {
            var basis = GetAttributeForRole(Terminal.Gui.Drawing.VisualRole.Normal);
            var width = Viewport.Width;
            var drawn = Start;

            Move(drawn, 0);

            foreach (var span in _chips.SelectMany(c => c.Spans))
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
            if (mouse.Position is not { } at)
            {
                return false;
            }

            if (mouse.Flags.HasFlag(MouseFlags.PositionReport))
            {
                Hover(Hovered(_chips, at.X - Start));
                return false;
            }

            if (!mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked))
            {
                return false;
            }

            if (Hovered(_chips, at.X - Start) is var index and >= 0)
            {
                _chips[index].Run();
                return true;
            }

            return false;
        }
    }
}
