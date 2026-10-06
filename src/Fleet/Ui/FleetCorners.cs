using Fleet.Ui.Constants;
using Fleet.Ui.Models;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Fleet.Ui;

public static class FleetCorners
{
    public static IReadOnlyList<FleetSpan> Help(bool keysShown, string revealKey) =>
    [
        new FleetSpan(FleetIcons.Info, FleetTones.Icon),
        FleetSpan.Plain(" "),
        keysShown ? FleetSpan.Plain(new string(' ', revealKey.Length)) : new FleetSpan(revealKey, FleetTones.Key),
    ];

    public static IReadOnlyList<FleetSpan> Close() => [new FleetSpan(FleetIcons.Close, FleetTones.Icon)];

    public static void Attach(View window, Action close, View? between = null)
    {
        var help = new Corner(() => Help(FleetKeyHints.Shown, FleetKeyHints.RevealKey), FleetKeyHints.Toggle)
        {
            X = 1,
            Y = 0,
        };

        var shut = new Corner(Close, close)
        {
            X = Pos.AnchorEnd(2),
            Y = 0,
        };

        window.Add(help, shut);

        if (between is not null)
        {
            between.X = Pos.Right(help) + 1;
            between.Width = Dim.Fill(3);
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
