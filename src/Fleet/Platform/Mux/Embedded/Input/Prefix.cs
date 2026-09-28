using Fleet.Platform.Mux.Embedded.Native;

namespace Fleet.Platform.Mux.Embedded.Input;

public enum PrefixCommand
{
    None,
    Armed,
    SendPrefix,
    Chord,
}

public sealed class Prefix(char letter)
{
    private bool _armed;

    public char Letter { get; } = char.ToLowerInvariant(letter);

    public byte ControlByte => (byte)(Letter - 'a' + 1);

    public Key Key => Key.A + (Letter - 'a');

    public bool Armed => _armed;

    public string Label => $"ctrl+{Letter}";

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
            _armed = isPrefix;
            return isPrefix ? PrefixCommand.Armed : PrefixCommand.None;
        }

        _armed = false;
        return isPrefix ? PrefixCommand.SendPrefix : PrefixCommand.Chord;
    }

    public PrefixCommand OnByte(byte b)
    {
        if (!_armed)
        {
            _armed = b == ControlByte;
            return _armed ? PrefixCommand.Armed : PrefixCommand.None;
        }

        _armed = false;
        return b == ControlByte ? PrefixCommand.SendPrefix : PrefixCommand.Chord;
    }

    public static string? CommandFor(Key key) => key switch
    {
        Key.Q or Key.D => "detach",
        Key.Space or Key.M => "menu",
        Key.N or Key.Tab => "next-tab",
        Key.P => "prev-tab",
        Key.S => "next-workspace",
        Key.H or Key.ArrowLeft => "focus-left",
        Key.J or Key.ArrowDown => "focus-down",
        Key.K or Key.ArrowUp => "focus-up",
        Key.L or Key.ArrowRight => "focus-right",
        Key.R => "redraw",
        Key.F => "float-new",
        Key.W => "float-toggle",
        Key.E => "float-embed",
        Key.G => "float-mode",
        Key.BracketLeft => "copy-mode",
        _ => null,
    };

    public static string? CommandFor(byte b) => char.ToLowerInvariant((char)b) switch
    {
        >= 'a' and <= 'z' and var c => CommandFor(Key.A + (c - 'a')),
        ' ' => "menu",
        '\t' => "next-tab",
        '[' => "copy-mode",
        _ => null,
    };
}
