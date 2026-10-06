using Fleet.Ui.Constants;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Fleet.Ui;

public sealed class FleetTabBar
{
    private const int Gap = 4;

    private const char Rule = '═';

    private const int MarkerRoom = 2;

    private readonly Label[] _labels;
    private readonly Label _underline;
    private readonly Label _moreLeft;
    private readonly Label _moreRight;
    private readonly View _strip;
    private readonly string[] _titles;
    private int _scroll;

    public FleetTabBar(Pos x, Pos y, IReadOnlyList<string> titles)
    {
        _titles = [.. titles];

        _labels = [.. _titles.Select(t => new Label
        {
            Y = 0,
            Text = t,
            SchemeName = FleetSchemes.Hint,
        })];

        _underline = new Label
        {
            Y = 1,
            Text = string.Empty,
            SchemeName = FleetSchemes.Section,
        };

        _moreLeft = new Label
        {
            X = 0,
            Y = 0,
            Text = FleetGlyphs.MoreLeft,
            SchemeName = FleetSchemes.Hint,
            Visible = false,
        };

        _moreRight = new Label
        {
            X = Pos.AnchorEnd(1),
            Y = 0,
            Text = FleetGlyphs.MoreRight,
            SchemeName = FleetSchemes.Hint,
            Visible = false,
        };

        _strip = new View
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 2,
        };

        Root = new View
        {
            X = x,
            Y = y,
            Width = Dim.Fill(1),
            Height = 2,
        };

        for (var i = 0; i < _labels.Length; i++)
        {
            var index = i;
            _labels[i].MouseEvent += (_, mouse) =>
            {
                if (mouse.Flags.HasFlag(MouseFlags.LeftButtonPressed) || mouse.IsSingleClicked)
                {
                    mouse.Handled = true;
                    Chosen?.Invoke(index);
                }
            };

            _strip.Add(_labels[i]);
        }

        _strip.Add(_underline);
        Root.Add(_strip, _moreLeft, _moreRight);
        Root.FrameChanged += (_, _) => Arrange();

        Arrange();
    }

    public View Root { get; }

    public event Action<int>? Chosen;

    public void Choose(int index) => Chosen?.Invoke(index);

    public int Selected { get; private set; }

    public int Count => _titles.Length;

    public void Select(int index)
    {
        if (index < 0 || index >= _titles.Length)
        {
            return;
        }

        Selected = index;
        Arrange();
    }

    public void Retitle(int index, string title)
    {
        if (index < 0 || index >= _titles.Length)
        {
            return;
        }

        _titles[index] = title;
        Arrange();
    }

    private void Arrange()
    {
        var starts = new int[_titles.Length];
        var total = 0;

        for (var i = 0; i < _titles.Length; i++)
        {
            starts[i] = total;
            total += _titles[i].Length + (i < _titles.Length - 1 ? Gap : 0);
        }

        var width = Root.Viewport.Width;
        var overflows = width > 0 && total > width;
        var room = overflows ? Math.Max(1, width - 2 * MarkerRoom) : width;

        if (overflows && Selected < _titles.Length)
        {
            var start = starts[Selected];
            var end = start + _titles[Selected].Length;
            _scroll = Math.Max(_scroll, end - room);
            _scroll = Math.Min(_scroll, start);
            _scroll = Math.Clamp(_scroll, 0, total - room);
        }
        else
        {
            _scroll = 0;
        }

        _strip.X = overflows ? MarkerRoom : 0;
        _strip.Width = overflows ? Dim.Fill(MarkerRoom) : Dim.Fill();
        _moreLeft.Visible = overflows && _scroll > 0;
        _moreRight.Visible = overflows && _scroll + room < total;

        for (var i = 0; i < _labels.Length; i++)
        {
            var title = _titles[i];
            var offset = starts[i] - _scroll;

            _labels[i].Text = title;
            _labels[i].X = Pos.Absolute(offset);
            _labels[i].Width = Dim.Absolute(title.Length);
            _labels[i].SchemeName = i == Selected ? FleetSchemes.Section : FleetSchemes.Hint;

            if (i == Selected)
            {
                _underline.X = Pos.Absolute(offset);
                _underline.Width = Dim.Absolute(title.Length);
                _underline.Text = new string(Rule, title.Length);
            }
        }

        Root.SetNeedsLayout();
        Root.SetNeedsDraw();
    }
}
