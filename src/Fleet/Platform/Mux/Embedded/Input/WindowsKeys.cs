using System.Runtime.InteropServices;
using System.Text;
using Fleet.Platform.Mux.Embedded.Native;
using Fleet.Platform.Mux.Embedded.Host;

namespace Fleet.Platform.Mux.Embedded.Input;

public sealed partial class WindowsKeys(Func<ushort, bool>? isDeadKey = null)
{
    private const uint RightAlt = 0x0001;
    private const uint LeftAlt = 0x0002;
    private const uint RightCtrl = 0x0004;
    private const uint LeftCtrl = 0x0008;
    private const uint Shift = 0x0010;
    private const uint NumLockOn = 0x0020;
    private const uint CapsLockOn = 0x0080;
    private const uint Enhanced = 0x0100;

    private char? _highSurrogate;

    public IEnumerable<KeyInput> Translate(WindowsConsole.KeyEventRecord record)
    {
        var vk = record.VirtualKeyCode;
        var down = record.KeyDown != 0;
        var unit = record.UnicodeChar;

        if (vk == 0x12 && !down && unit != 0)
        {
            yield return KeyInput.Text(unit.ToString());
            yield break;
        }

        if (IsModifierOnly(vk) && unit == 0)
        {
            yield break;
        }

        var text = Text(unit);

        if (vk == 0)
        {
            if (down && text is not null)
            {
                yield return KeyInput.Text(text);
            }

            yield break;
        }

        if (char.IsHighSurrogate(unit))
        {
            yield break;
        }

        var mods = Mods(record.ControlKeyState);

        if (unit == 0 && (isDeadKey ?? IsDeadKey)(vk))
        {
            yield return new KeyInput(Key.Unidentified, mods, Native.Mods.None, null,
                down ? KeyAction.Press : KeyAction.Release, 0, vk);
            yield break;
        }

        var key = MapKey(vk, (record.ControlKeyState & Enhanced) != 0);

        if ((mods & Native.Mods.Ctrl) != 0)
        {
            text = null;
        }

        var consumed = text is not null && (mods & Native.Mods.Shift) != 0
            ? Native.Mods.Shift
            : Native.Mods.None;

        var action = !down ? KeyAction.Release : KeyAction.Press;
        var repeats = Math.Max((int)record.RepeatCount, 1);

        for (var i = 0; i < repeats; i++)
        {
            yield return new KeyInput(
                key,
                mods,
                consumed,
                text,
                i == 0 ? action : KeyAction.Repeat,
                Unshifted(vk),
                vk);
        }
    }

    private string? Text(char unit)
    {
        if (char.IsHighSurrogate(unit))
        {
            _highSurrogate = unit;
            return null;
        }

        if (char.IsLowSurrogate(unit) && _highSurrogate is { } high)
        {
            _highSurrogate = null;
            return new string([high, unit]);
        }

        _highSurrogate = null;
        return unit >= 0x20 && unit != 0x7f ? unit.ToString() : null;
    }

    private static Native.Mods Mods(uint state)
    {
        var altGr = (state & RightAlt) != 0 && (state & LeftCtrl) != 0 && (state & RightCtrl) == 0;

        var mods = Native.Mods.None;
        if ((state & Shift) != 0) mods |= Native.Mods.Shift;
        if (!altGr && (state & (LeftCtrl | RightCtrl)) != 0) mods |= Native.Mods.Ctrl;
        if ((state & LeftAlt) != 0 || ((state & RightAlt) != 0 && !altGr)) mods |= Native.Mods.Alt;
        if ((state & CapsLockOn) != 0) mods |= Native.Mods.CapsLock;
        if ((state & NumLockOn) != 0) mods |= Native.Mods.NumLock;
        return mods;
    }

    private static bool IsModifierOnly(ushort vk) =>
        vk is 0x10 or 0x11 or 0x12 or 0x14 or 0x5B or 0x5C or 0x90 or 0x91 or (>= 0xA0 and <= 0xA5);

    private static bool IsDeadKey(ushort vk) =>
        vk is not (>= 0x41 and <= 0x5A or >= 0x30 and <= 0x39) && (MapVirtualKeyW(vk, 2) & 0x80000000) != 0;

    private static uint Unshifted(ushort vk)
    {
        if (vk is >= 0x41 and <= 0x5A)
        {
            return (uint)(vk + 0x20);
        }

        if (vk is >= 0x30 and <= 0x39 or 0x20)
        {
            return vk;
        }

        var mapped = MapVirtualKeyW(vk, 2) & 0x7FFF;
        return mapped is >= 0x20 and < 0x7F ? (uint)char.ToLowerInvariant((char)mapped) : 0;
    }

    private static Key MapKey(ushort vk, bool enhanced) => vk switch
    {
        >= 0x41 and <= 0x5A => Key.A + (vk - 0x41),
        >= 0x30 and <= 0x39 => Key.Digit0 + (vk - 0x30),
        >= 0x60 and <= 0x69 => Key.Numpad0 + (vk - 0x60),
        >= 0x70 and <= 0x87 => Key.F1 + (vk - 0x70),
        0x08 => Key.Backspace,
        0x09 => Key.Tab,
        0x0D => enhanced ? Key.NumpadEnter : Key.Enter,
        0x1B => Key.Escape,
        0x20 => Key.Space,
        0x21 => Key.PageUp,
        0x22 => Key.PageDown,
        0x23 => Key.End,
        0x24 => Key.Home,
        0x25 => Key.ArrowLeft,
        0x26 => Key.ArrowUp,
        0x27 => Key.ArrowRight,
        0x28 => Key.ArrowDown,
        0x2D => Key.Insert,
        0x2E => Key.Delete,
        0x5D => Key.ContextMenu,
        0x6A => Key.NumpadMultiply,
        0x6B => Key.NumpadAdd,
        0x6C => Key.NumpadSeparator,
        0x6D => Key.NumpadSubtract,
        0x6E => Key.NumpadDecimal,
        0x6F => Key.NumpadDivide,
        0xBA => Key.Semicolon,
        0xBB => Key.Equal,
        0xBC => Key.Comma,
        0xBD => Key.Minus,
        0xBE => Key.Period,
        0xBF => Key.Slash,
        0xC0 => Key.Backquote,
        0xDB => Key.BracketLeft,
        0xDC => Key.Backslash,
        0xDD => Key.BracketRight,
        0xDE => Key.Quote,
        0xE2 => Key.IntlBackslash,
        _ => Key.Unidentified,
    };

    [LibraryImport("user32.dll")]
    private static partial uint MapVirtualKeyW(uint code, uint mapType);
}

public readonly record struct KeyInput(
    Key Key,
    Mods Mods,
    Mods Consumed,
    string? Utf8,
    KeyAction Action,
    uint Unshifted,
    ushort VirtualKey)
{
    public bool IsRawText => Key == Key.Unidentified && VirtualKey == 0;

    public static KeyInput Text(string text) =>
        new(Key.Unidentified, Mods.None, Mods.None, text, KeyAction.Press, 0, 0);
}
