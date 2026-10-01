using Fleet.Platform.Mux.Embedded.Host;
using Fleet.Platform.Mux.Embedded.Protocol;

namespace Fleet.Platform.Mux.Embedded.Input;

public sealed class WindowsMouse
{
    private const uint LeftButton = 0x1;
    private const uint RightButton = 0x2;
    private const uint MiddleButton = 0x4;
    private const uint Moved = 0x1;
    private const uint Wheeled = 0x4;
    private const uint HorizontalWheeled = 0x8;

    private uint _held;
    private (int X, int Y) _lastMotion = (-1, -1);

    public IEnumerable<MouseMessage> Translate(WindowsConsole.MouseEventRecord record, int originX = 0, int originY = 0)
    {
        var x = record.X - originX;
        var y = record.Y - originY;
        var mods = Mods(record.ControlKeyState);

        if ((record.EventFlags & (Wheeled | HorizontalWheeled)) != 0)
        {
            var delta = (short)(record.ButtonState >> 16);
            var button = (record.EventFlags & Wheeled) != 0
                ? delta > 0 ? MouseButtons.WheelUp : MouseButtons.WheelDown
                : delta > 0 ? MouseButtons.WheelRight : MouseButtons.WheelLeft;

            yield return new MouseMessage { X = x, Y = y, Button = button, Action = MouseActions.Press, Mods = mods, Held = _held != 0 };
            yield break;
        }

        var buttons = record.ButtonState & (LeftButton | RightButton | MiddleButton);
        var changed = buttons ^ _held;

        foreach (var (bit, button) in new[] { (LeftButton, MouseButtons.Left), (RightButton, MouseButtons.Right), (MiddleButton, MouseButtons.Middle) })
        {
            if ((changed & bit) == 0)
            {
                continue;
            }

            var pressed = (buttons & bit) != 0;
            _held = pressed ? _held | bit : _held & ~bit;
            _lastMotion = (x, y);

            yield return new MouseMessage
            {
                X = x,
                Y = y,
                Button = button,
                Action = pressed ? MouseActions.Press : MouseActions.Release,
                Mods = mods,
                Held = _held != 0,
            };
        }

        if (changed == 0 && (record.EventFlags & Moved) != 0 && (x, y) != _lastMotion)
        {
            _lastMotion = (x, y);

            yield return new MouseMessage
            {
                X = x,
                Y = y,
                Button = (_held & LeftButton) != 0 ? MouseButtons.Left
                    : (_held & MiddleButton) != 0 ? MouseButtons.Middle
                    : (_held & RightButton) != 0 ? MouseButtons.Right
                    : MouseButtons.None,
                Action = MouseActions.Motion,
                Mods = mods,
                Held = _held != 0,
            };
        }
    }

    private static int Mods(uint state)
    {
        var mods = 0;
        if ((state & 0x0010) != 0) mods |= 1;
        if ((state & (0x0004 | 0x0008)) != 0) mods |= 2;
        if ((state & (0x0001 | 0x0002)) != 0) mods |= 4;
        return mods;
    }
}
