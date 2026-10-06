using Fleet.Ui.Constants;
using Fleet.Ui.Models;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Fleet.Ui;

public static class FleetCorners
{
    public static IReadOnlyList<FleetSpan> Help(bool keysShown, string revealKey) => keysShown
        ?
        [
            new FleetSpan(FleetGlyphs.PillLeft, FleetTones.ChipEdge),
            new FleetSpan($" {FleetIcons.Info} ", FleetTones.ChipLabel),
            new FleetSpan(FleetGlyphs.PillRight, FleetTones.ChipEdge),
            FleetSpan.Plain(new string(' ', revealKey.Length + 1)),
        ]
        :
        [
            new FleetSpan(FleetGlyphs.PillLeft, FleetTones.ChipEdge),
            new FleetSpan($" {revealKey} ", FleetTones.ChipKey),
            new FleetSpan($"{FleetIcons.Info} ", FleetTones.ChipLabel),
            new FleetSpan(FleetGlyphs.PillRight, FleetTones.ChipEdge),
        ];

    public const string CloseKey = "esc";

    public static IReadOnlyList<FleetSpan> Close(bool keysShown) => keysShown
        ?
        [
            new FleetSpan(FleetGlyphs.PillLeft, FleetTones.ChipEdge),
            new FleetSpan($" {CloseKey} ", FleetTones.ChipKey),
            new FleetSpan($"{FleetIcons.Close} ", FleetTones.ChipLabel),
            new FleetSpan(FleetGlyphs.PillRight, FleetTones.ChipEdge),
        ]
        :
        [
            FleetSpan.Plain(new string(' ', CloseKey.Length + 1)),
            new FleetSpan(FleetGlyphs.PillLeft, FleetTones.ChipEdge),
            new FleetSpan($" {FleetIcons.Close} ", FleetTones.ChipLabel),
            new FleetSpan(FleetGlyphs.PillRight, FleetTones.ChipEdge),
        ];

    public const int Margin = 1;

    public static int Rows => FloatBorder.Enabled ? 0 : 2 * Margin;

    public static void Attach(View window, Action close, View? below = null)
    {
        if (FloatBorder.Enabled)
        {
            FloatBorder.Corners(window, close);

            if (below is not null)
            {
                below.X = 1;
                below.Y = 0;
                below.Width = Dim.Fill(1);
            }

            return;
        }

        if (window.Padding is { } padding)
        {
            padding.Thickness = new Thickness(0, Margin, 0, Margin);
        }

        var help = new Corner(() => Help(FleetKeyHints.Shown, FleetKeyHints.RevealKey), FleetKeyHints.Toggle)
        {
            X = 1,
            Y = 0,
        };

        var shut = new Corner(() => Close(FleetKeyHints.Shown), close)
        {
            X = Pos.AnchorEnd(10),
            Y = 0,
        };

        window.Add(help, shut);

        if (below is not null)
        {
            below.X = 1;
            below.Y = Pos.Bottom(help);
            below.Width = Dim.Fill(1);
        }
    }

    private sealed class Corner : View
    {
        private readonly Func<IReadOnlyList<FleetSpan>> _spans;

        private readonly Action _run;

        public Corner(Func<IReadOnlyList<FleetSpan>> spans, Action run)
        {
            _spans = spans;
            _run = run;
            Height = 1;
            CanFocus = false;
            SchemeName = FleetSchemes.Screen;
            Fit();
            FleetKeyHints.Changed += Refresh;
        }

        private void Fit() => Width = _spans().Sum(s => s.Text.EnumerateRunes().Count());

        private void Refresh()
        {
            Fit();
            SetNeedsLayout();
            SetNeedsDraw();
        }

        protected override void Dispose(bool disposing)
        {
            FleetKeyHints.Changed -= Refresh;
            base.Dispose(disposing);
        }

        protected override bool OnDrawingContent(DrawContext? context)
        {
            var basis = GetAttributeForRole(Terminal.Gui.Drawing.VisualRole.Normal);

            Move(0, 0);

            foreach (var span in _spans())
            {
                SetAttribute(FleetInk.For(span.Tone, basis));
                AddStr(span.Text);
            }

            return true;
        }

        protected override bool OnMouseEvent(Mouse mouse)
        {
            if (!mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked))
            {
                return false;
            }

            _run();
            return true;
        }
    }
}
