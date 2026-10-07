using System.Drawing;
using Fleet.Shared.Settings.Enums;
using Fleet.Ui.Constants;
using Fleet.Ui.Models;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Fleet.Ui;

public static class FleetCorners
{
    public static IReadOnlyList<FleetSpan> Help(bool keysShown, string revealKey, ButtonHints hints = ButtonHints.Tooltips) => keysShown
        ?
        [
            new FleetSpan(FleetGlyphs.PillLeft, FleetTones.ChipEdge),
            new FleetSpan($" {FleetButtonHints.Face(FleetIcons.Info, hints)} ", FleetTones.ChipLabel),
            new FleetSpan(FleetGlyphs.PillRight, FleetTones.ChipEdge),
            FleetSpan.Plain(new string(' ', revealKey.Length + 1)),
        ]
        :
        [
            new FleetSpan(FleetGlyphs.PillLeft, FleetTones.ChipEdge),
            new FleetSpan($" {revealKey} ", FleetTones.ChipKey),
            new FleetSpan($"{FleetButtonHints.Face(FleetIcons.Info, hints)} ", FleetTones.ChipLabel),
            new FleetSpan(FleetGlyphs.PillRight, FleetTones.ChipEdge),
        ];

    public const string CloseKey = "esc";

    public static IReadOnlyList<FleetSpan> Close(bool keysShown, ButtonHints hints = ButtonHints.Tooltips) => keysShown
        ?
        [
            new FleetSpan(FleetGlyphs.PillLeft, FleetTones.ChipEdge),
            new FleetSpan($" {CloseKey} ", FleetTones.ChipKey),
            new FleetSpan($"{FleetButtonHints.Face(FleetIcons.Close, hints)} ", FleetTones.ChipLabel),
            new FleetSpan(FleetGlyphs.PillRight, FleetTones.ChipEdge),
        ]
        :
        [
            FleetSpan.Plain(new string(' ', CloseKey.Length + 1)),
            new FleetSpan(FleetGlyphs.PillLeft, FleetTones.ChipEdge),
            new FleetSpan($" {FleetButtonHints.Face(FleetIcons.Close, hints)} ", FleetTones.ChipLabel),
            new FleetSpan(FleetGlyphs.PillRight, FleetTones.ChipEdge),
        ];

    public static string Tip(string icon, string key) => FleetToolTip.Label(FleetIcons.Name(icon), key);

    public const int Margin = 1;

    public static int Rows => FloatBorder.Enabled ? 0 : 2 * Margin;

    public static void Attach(View window, Action? close, View? below = null, bool framed = false)
    {
        if (FloatBorder.Enabled || (framed && FloatBorder.Framing))
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

        var help = new Corner(
            () => Help(FleetKeyHints.Shown, FleetKeyHints.RevealKey, FleetButtonHints.Mode), FleetIcons.Info, FleetKeyHints.Toggle, () => FleetKeyHints.RevealKey)
        {
            X = 1,
            Y = 0,
        };

        window.Add(help);

        if (close is not null)
        {
            window.Add(new Corner(() => Close(FleetKeyHints.Shown, FleetButtonHints.Mode), FleetIcons.Close, close, () => string.Empty, anchorEnd: true)
            {
                Y = 0,
            });
        }

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

        private readonly string _icon;

        private readonly Func<string> _key;

        private readonly bool _anchorEnd;

        private bool _hovered;

        public Corner(Func<IReadOnlyList<FleetSpan>> spans, string icon, Action run, Func<string> key, bool anchorEnd = false)
        {
            _key = key;
            _spans = spans;
            _icon = icon;
            _run = run;
            _anchorEnd = anchorEnd;
            Height = 1;
            CanFocus = false;
            MousePositionTracking = true;
            SchemeName = FleetSchemes.Screen;
            Fit();
            FleetKeyHints.Changed += Refresh;
            FleetButtonHints.Changed += Refresh;
            MouseLeave += (_, _) => Hover(false);
        }

        private void Fit()
        {
            var width = _spans().Sum(s => s.Text.EnumerateRunes().Count());

            Width = width;

            if (_anchorEnd)
            {
                X = Pos.AnchorEnd(width + FleetCorners.Margin);
            }
        }

        private void Hover(bool on)
        {
            if (on == _hovered)
            {
                return;
            }

            _hovered = on;

            if (on)
            {
                var at = ViewportToScreen(new Rectangle(0, 0, 1, 1)).Location;

                FleetToolTip.Show(this, new Point(at.X, at.Y + 1), Tip(_icon, _key()));
            }
            else
            {
                FleetToolTip.Hide();
            }
        }

        private void Refresh()
        {
            Hover(false);
            Fit();
            SetNeedsLayout();
            SetNeedsDraw();
        }

        protected override void Dispose(bool disposing)
        {
            FleetKeyHints.Changed -= Refresh;
            FleetButtonHints.Changed -= Refresh;
            Hover(false);
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
            if (mouse.Flags.HasFlag(MouseFlags.PositionReport))
            {
                Hover(true);
                return false;
            }

            if (!mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked))
            {
                return false;
            }

            _run();
            return true;
        }
    }
}
