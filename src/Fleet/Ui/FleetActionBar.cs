using Fleet.Ui.Constants;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Fleet.Ui;

public sealed class FleetActionBar
{
    private static readonly System.Text.Rune NoHotKey = (System.Text.Rune)'￿';

    private readonly List<Button> _buttons = [];

    public FleetActionBar(Pos y)
    {
        Root = new View
        {
            X = 1,
            Y = y,
            Width = Dim.Fill(1),
            Height = 1,
            CanFocus = false,
        };
    }

    public View Root { get; }

    public void Show(IReadOnlyList<(string Key, string Label, Action Run)> items)
    {
        foreach (var button in _buttons)
        {
            Root.Remove(button);
            button.Dispose();
        }

        _buttons.Clear();

        var offset = 0;

        foreach (var (key, label, run) in items)
        {
            var text = key.Length == 0 ? label : $"{key} {label}";

            var button = new Button
            {
                X = Pos.Absolute(offset),
                Y = 0,
                Text = text,
                NoDecorations = true,
                ShadowStyle = ShadowStyles.None,
                HotKeySpecifier = NoHotKey,
                CanFocus = false,
                SchemeName = FleetSchemes.Hint,
            };

            button.MouseEvent += (_, e) =>
            {
                if (e.Flags.HasFlag(MouseFlags.LeftButtonClicked))
                {
                    run();
                    e.Handled = true;
                }
            };

            _buttons.Add(button);
            Root.Add(button);

            offset += text.Length + 3;
        }

        Root.SetNeedsLayout();
        Root.SetNeedsDraw();
    }
}
