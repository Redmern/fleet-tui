using Fleet.Ui.Constants;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Fleet.Ui;

public sealed class FleetTabBar
{
    private const int Gap = 4;

    private const char Rule = '═';

    private readonly Label[] _labels;
    private readonly Label _underline;
    private readonly string[] _titles;

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

        Root = new View
        {
            X = x,
            Y = y,
            Width = Dim.Fill(1),
            Height = 2,
        };

        foreach (var label in _labels)
        {
            Root.Add(label);
        }

        Root.Add(_underline);

        Arrange();
    }

    public View Root { get; }

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
        var offset = 0;

        for (var i = 0; i < _labels.Length; i++)
        {
            var title = _titles[i];

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

            offset += title.Length + Gap;
        }

        Root.SetNeedsLayout();
        Root.SetNeedsDraw();
    }
}
