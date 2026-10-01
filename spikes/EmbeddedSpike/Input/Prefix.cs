using EmbeddedSpike.Ghostty;

namespace EmbeddedSpike.Input;

internal enum PrefixCommand
{
    None,
    Armed,
    Quit,
    SendPrefix,
    Dump,
    Redraw,
    Unbound,
}

// tmux-style prefix: Ctrl+<letter>, then one command key. Prefix twice sends the
// prefix chord itself to the pane.
internal sealed class Prefix(char letter)
{
    private bool _armed;

    public char Letter { get; } = char.ToLowerInvariant(letter);

    public byte ControlByte => (byte)(Letter - 'a' + 1);

    public Key Key => Key.A + (Letter - 'a');

    public bool Armed => _armed;

    public static Prefix Parse(string spec)
    {
        var s = spec.Trim().ToLowerInvariant();
        if (s.StartsWith("ctrl+", StringComparison.Ordinal) && s.Length == 6 && s[5] is >= 'a' and <= 'z')
        {
            return new Prefix(s[5]);
        }

        throw new ArgumentException($"prefix must be ctrl+<letter>, got '{spec}'");
    }

    public PrefixCommand OnKey(Key key, Mods mods)
    {
        var chord = mods & (Mods.Ctrl | Mods.Alt | Mods.Shift | Mods.Super);
        var isPrefix = key == Key && chord == Mods.Ctrl;

        if (!_armed)
        {
            if (isPrefix)
            {
                _armed = true;
                return PrefixCommand.Armed;
            }

            return PrefixCommand.None;
        }

        _armed = false;
        if (isPrefix)
        {
            return PrefixCommand.SendPrefix;
        }

        if (chord is Mods.None)
        {
            return key switch
            {
                Key.Q => PrefixCommand.Quit,
                Key.D => PrefixCommand.Dump,
                Key.R => PrefixCommand.Redraw,
                _ => PrefixCommand.Unbound,
            };
        }

        return PrefixCommand.Unbound;
    }

    public PrefixCommand OnByte(byte b)
    {
        if (!_armed)
        {
            if (b == ControlByte)
            {
                _armed = true;
                return PrefixCommand.Armed;
            }

            return PrefixCommand.None;
        }

        _armed = false;
        return b switch
        {
            _ when b == ControlByte => PrefixCommand.SendPrefix,
            (byte)'q' => PrefixCommand.Quit,
            (byte)'d' => PrefixCommand.Dump,
            (byte)'r' => PrefixCommand.Redraw,
            _ => PrefixCommand.Unbound,
        };
    }
}
