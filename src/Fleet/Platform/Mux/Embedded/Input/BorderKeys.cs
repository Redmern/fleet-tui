using Fleet.Platform.Mux.Embedded.Native;
using Fleet.Platform.Mux.Embedded.Protocol;

namespace Fleet.Platform.Mux.Embedded.Input;

public static class BorderKeys
{
    private const uint ShiftPressed = 0x10;
    private const uint CtrlPressed = 0x08;
    private const uint AltPressed = 0x02;

    public static KeyChord? Chord(string spec)
    {
        try
        {
            return KeyChord.Parse(spec);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static KeyMessage Message(KeyChord chord) => new()
    {
        Key = (int)chord.Key,
        Mods = (int)chord.Mods,
        Text = chord.Text,
        Action = (int)KeyAction.Press,
    };

    public static byte[]? Win32(KeyChord chord)
    {
        if (chord.Text is not null || VirtualKey(chord.Key) is not { } vk)
        {
            return null;
        }

        var state = (chord.Mods.HasFlag(Mods.Shift) ? ShiftPressed : 0)
                    | (chord.Mods.HasFlag(Mods.Ctrl) ? CtrlPressed : 0)
                    | (chord.Mods.HasFlag(Mods.Alt) ? AltPressed : 0);
        var unicode = Unicode(chord);

        return [.. ConPtyModes.Encode(vk, 0, unicode, true, state, 1), .. ConPtyModes.Encode(vk, 0, unicode, false, state, 1)];
    }

    private static int? VirtualKey(Key key) => key switch
    {
        Key.Enter => 0x0D,
        Key.Escape => 0x1B,
        Key.Backspace => 0x08,
        Key.Tab => 0x09,
        Key.Space => 0x20,
        Key.PageUp => 0x21,
        Key.PageDown => 0x22,
        Key.End => 0x23,
        Key.Home => 0x24,
        Key.ArrowLeft => 0x25,
        Key.ArrowUp => 0x26,
        Key.ArrowRight => 0x27,
        Key.ArrowDown => 0x28,
        Key.Insert => 0x2D,
        Key.Delete => 0x2E,
        >= Key.Digit0 and <= Key.Digit9 => 0x30 + (key - Key.Digit0),
        >= Key.A and <= Key.Z => 0x41 + (key - Key.A),
        >= Key.F1 and <= Key.F12 => 0x70 + (key - Key.F1),
        _ => null,
    };

    private static int Unicode(KeyChord chord) => chord.Key switch
    {
        Key.Enter => '\r',
        Key.Escape => 0x1B,
        Key.Backspace => 0x08,
        Key.Tab => '\t',
        Key.Space => ' ',
        >= Key.Digit0 and <= Key.Digit9 => '0' + (chord.Key - Key.Digit0),
        >= Key.A and <= Key.Z when chord.Mods.HasFlag(Mods.Ctrl) => 1 + (chord.Key - Key.A),
        >= Key.A and <= Key.Z => (chord.Mods.HasFlag(Mods.Shift) ? 'A' : 'a') + (chord.Key - Key.A),
        _ => 0,
    };
}
