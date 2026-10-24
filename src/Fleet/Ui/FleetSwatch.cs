using Fleet.Shared.Themes;
using Fleet.Ui.Constants;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;

using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Fleet.Ui;

public sealed class FleetSwatch : View
{
    private string _color;

    public FleetSwatch(Pos x, Pos y, int width, int height, string color)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
        CanFocus = false;
        SchemeName = FleetSchemes.Screen;
        _color = HexColor.Normalize(color) ?? "#000000";
    }

    public string Hex
    {
        get => _color;
        set
        {
            _color = HexColor.Normalize(value) ?? _color;
            SetNeedsDraw();
        }
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var normal = GetAttributeForRole(VisualRole.Normal);

        SetAttribute(new Attribute(normal.Foreground, new Color(_color), TextStyle.None));

        for (var row = 0; row < Viewport.Height; row++)
        {
            Move(0, row);

            for (var col = 0; col < Viewport.Width; col++)
            {
                AddRune((System.Text.Rune)' ');
            }
        }

        return true;
    }
}
