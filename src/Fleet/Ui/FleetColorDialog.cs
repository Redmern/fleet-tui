using System.Globalization;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Themes;
using Fleet.Ui.Constants;
using Fleet.Ui.Models;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Fleet.Ui;

public static class FleetColorDialog
{
    public const int Red = 0;

    public const int Green = 1;

    public const int Blue = 2;

    public const int BigStep = 16;

    public const string InvalidHex = "not a color: type six hex digits, like #1e1e2e";

    private const int SliderWidth = 32;

    private static readonly string[] ChannelNames = ["R", "G", "B"];

    public static (int R, int G, int B) Channels(string hex)
    {
        var color = HexColor.Normalize(hex) ?? throw new ArgumentException($"not a hex color: {hex}", nameof(hex));

        return (Channel(color, 1), Channel(color, 3), Channel(color, 5));
    }

    public static string Hex(int r, int g, int b) =>
        $"#{Math.Clamp(r, 0, 255):x2}{Math.Clamp(g, 0, 255):x2}{Math.Clamp(b, 0, 255):x2}";

    public static string Nudge(string hex, int channel, int delta)
    {
        var (r, g, b) = Channels(hex);

        return channel switch
        {
            Red => Hex(Math.Clamp(r + delta, 0, 255), g, b),
            Green => Hex(r, Math.Clamp(g + delta, 0, 255), b),
            Blue => Hex(r, g, Math.Clamp(b + delta, 0, 255)),
            _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "not a color channel"),
        };
    }

    public static int? Delta(Key key)
    {
        if (key == Key.CursorLeft)
        {
            return -1;
        }

        if (key == Key.CursorRight)
        {
            return 1;
        }

        if (key == Key.CursorLeft.WithShift || key == Key.PageDown)
        {
            return -BigStep;
        }

        if (key == Key.CursorRight.WithShift || key == Key.PageUp)
        {
            return BigStep;
        }

        return null;
    }

    public static string Typed(string current, string typed) => HexColor.Normalize(typed) ?? current;

    public static string Slider(int value, int width)
    {
        var filled = (int)Math.Round(Math.Clamp(value, 0, 255) * width / 255.0);

        return new string('█', filled) + new string('░', width - filled);
    }

    public static string? Show(IApplication app, Keymap keymap, string title, string hex)
    {
        var color = HexColor.Normalize(hex) ?? "#000000";
        var channel = Red;
        string? result = null;

        var window = FleetTheme.Modal(title, 60, 11 + FleetCorners.Rows);
        var swatch = new FleetSwatch(2, 1, 8, 3, color);
        var sliders = new FleetCanvas(12, 1, 3);
        var field = FleetTheme.Field(9, 5, color);
        var error = FleetTheme.ErrorText(2, 6);

        void Draw()
        {
            swatch.Hex = color;

            var (r, g, b) = Channels(color);
            int[] values = [r, g, b];

            sliders.Show(
                [.. values.Select((v, i) => (IReadOnlyList<FleetSpan>)
                [
                    new FleetSpan($"{ChannelNames[i]} ", FleetTones.Key),
                    FleetSpan.Plain(Slider(v, SliderWidth)),
                    FleetSpan.Muted($" {v,3}"),
                ])],
                (channel, 0, 1));
        }

        void TakeHex()
        {
            var typed = field.Text.Trim();

            if (HexColor.Normalize(typed) is null)
            {
                error.Text = InvalidHex;
                field.Text = color;
                return;
            }

            error.Text = string.Empty;
            color = Typed(color, typed);
            field.Text = color;
            Draw();
            sliders.SetFocus();
        }

        void Accept()
        {
            result = Typed(color, field.Text);
            app.RequestStop(window);
        }

        void Leave()
        {
            FleetModal.Back();
            app.RequestStop(window);
        }

        field.Accepting += (_, e) =>
        {
            e.Handled = true;
            TakeHex();
        };

        var bar = new FleetActionBar(Pos.AnchorEnd(1));

        bar.Show(
        [
            ("enter", FleetIcons.Select, Accept),
            ("tab", FleetIcons.Rename, () => field.SetFocus()),
            ("bksp", FleetIcons.Back, Leave),
        ]);

        var claim = FleetModal.Enter();

        void Keys(object? sender, Key key)
        {
            if (!FleetModal.Owns(claim))
            {
                return;
            }

            if (key == FleetKeys.Cancel)
            {
                app.RequestStop(window);
            }
            else if (key == Key.Tab || key == Key.Tab.WithShift)
            {
                if (field.HasFocus)
                {
                    TakeHex();
                }
                else
                {
                    field.SetFocus();
                }
            }
            else if (field.HasFocus)
            {
                return;
            }
            else if (key == Key.Enter)
            {
                Accept();
            }
            else if (FleetKeys.GoesBack(key))
            {
                Leave();
            }
            else if (key == Key.CursorUp || key == keymap.KeyFor(FleetAction.MoveUp))
            {
                channel = (channel + 2) % 3;
                Draw();
            }
            else if (key == Key.CursorDown || key == keymap.KeyFor(FleetAction.MoveDown))
            {
                channel = (channel + 1) % 3;
                Draw();
            }
            else if (Delta(key) is { } delta)
            {
                color = Nudge(color, channel, delta);
                field.Text = color;
                error.Text = string.Empty;
                Draw();
            }
            else
            {
                return;
            }

            key.Handled = true;
        }

        app.Keyboard.KeyDown += Keys;
        window.Add(swatch, sliders, FleetTheme.Caption(2, 5, "Hex"), field, error, bar.Root);
        FleetCorners.Attach(window, () => app.RequestStop(window));
        Draw();

        try
        {
            sliders.SetFocus();
            app.Run(window);
        }
        finally
        {
            FleetModal.Leave();
            app.Keyboard.KeyDown -= Keys;
            window.Dispose();
        }

        return result;
    }

    private static int Channel(string hex, int at) =>
        int.Parse(hex.AsSpan(at, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
}
