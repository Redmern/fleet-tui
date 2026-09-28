using System.Text;
using Fleet.Platform.Mux.Embedded.Native;

namespace Fleet.Platform.Mux.Embedded.Input;

public readonly record struct KeyChord(Mods Mods, Key Key, string? Text)
{
    private const Mods Chord = Mods.Ctrl | Mods.Alt | Mods.Shift | Mods.Super;

    public static KeyChord? Parse(string spec)
    {
        var trimmed = spec.Trim();

        if (trimmed.Length == 1)
        {
            return trimmed == " " ? new KeyChord(Mods.None, Key.Space, null) : new KeyChord(Mods.None, Key.Unidentified, trimmed);
        }

        if (trimmed.Length == 0)
        {
            return null;
        }

        var parts = trimmed.Split('+');
        var name = parts[^1].Length == 0 && trimmed.EndsWith("++", StringComparison.Ordinal) ? "+" : parts[^1];
        var mods = Mods.None;

        foreach (var mod in parts[..^1].Where(p => p.Length > 0))
        {
            mods |= mod.ToLowerInvariant() switch
            {
                "ctrl" or "control" => Mods.Ctrl,
                "alt" or "meta" or "option" => Mods.Alt,
                "shift" => Mods.Shift,
                "super" or "win" or "cmd" => Mods.Super,
                _ => throw new FormatException($"unknown modifier '{mod}' in '{spec}'"),
            };
        }

        if (mods == Mods.None && name.Length == 1)
        {
            return new KeyChord(Mods.None, Key.Unidentified, name);
        }

        return new KeyChord(mods, KeyFor(name) ?? throw new FormatException($"unknown key '{name}' in '{spec}'"), null);
    }

    public bool Matches(Key key, Mods mods, string? text)
    {
        if (Text is not null)
        {
            return text == Text && (mods & (Mods.Ctrl | Mods.Alt | Mods.Super)) == 0;
        }

        return key == Key && (mods & Chord) == Mods;
    }

    public byte[]? Bytes()
    {
        if (Text is not null)
        {
            return Encoding.UTF8.GetBytes(Text);
        }

        var ctrl = Mods.HasFlag(Mods.Ctrl);
        var alt = Mods.HasFlag(Mods.Alt);
        var shift = Mods.HasFlag(Mods.Shift);
        var modifier = 1 + (shift ? 1 : 0) + (alt ? 2 : 0) + (ctrl ? 4 : 0);

        string? Csi(char final) => modifier == 1 ? $"\e[{final}" : $"\e[1;{modifier}{final}";
        string? Tilde(int code) => modifier == 1 ? $"\e[{code}~" : $"\e[{code};{modifier}~";

        var special = Key switch
        {
            Key.ArrowUp => Csi('A'),
            Key.ArrowDown => Csi('B'),
            Key.ArrowRight => Csi('C'),
            Key.ArrowLeft => Csi('D'),
            Key.Home => Csi('H'),
            Key.End => Csi('F'),
            Key.PageUp => Tilde(5),
            Key.PageDown => Tilde(6),
            Key.Delete => Tilde(3),
            Key.Insert => Tilde(2),
            _ => null,
        };

        if (special is not null)
        {
            return Encoding.ASCII.GetBytes(special);
        }

        var basic = Key switch
        {
            Key.Enter when !ctrl && !shift => "\r",
            Key.Tab when shift && !ctrl => "\e[Z",
            Key.Tab when !ctrl => "\t",
            Key.Escape => "\e",
            Key.Space => ctrl ? "\0" : " ",
            Key.Backspace => ctrl ? "\b" : "\x7f",
            >= Key.A and <= Key.Z => ctrl
                ? ((char)(Key - Key.A + 1)).ToString()
                : ((char)((shift ? 'A' : 'a') + (Key - Key.A))).ToString(),
            >= Key.Digit0 and <= Key.Digit9 when !ctrl => ((char)('0' + (Key - Key.Digit0))).ToString(),
            Key.Slash when ctrl => "\x1f",
            Key.BracketLeft when ctrl => "\e",
            Key.Backslash when ctrl => "\x1c",
            Key.BracketRight when ctrl => "\x1d",
            _ => null,
        };

        if (basic is null)
        {
            return null;
        }

        return Encoding.ASCII.GetBytes(alt && Key is not Key.Escape ? "\e" + basic : basic);
    }

    public string Label
    {
        get
        {
            if (Text is not null)
            {
                return Text;
            }

            var name = Key switch
            {
                >= Key.A and <= Key.Z => ((char)('a' + (Key - Key.A))).ToString(),
                >= Key.Digit0 and <= Key.Digit9 => ((char)('0' + (Key - Key.Digit0))).ToString(),
                Key.ArrowLeft => "←",
                Key.ArrowRight => "→",
                Key.ArrowUp => "↑",
                Key.ArrowDown => "↓",
                Key.Escape => "esc",
                _ => Key.ToString().ToLowerInvariant(),
            };

            var mods = new List<string>();
            if (Mods.HasFlag(Mods.Ctrl)) mods.Add("ctrl");
            if (Mods.HasFlag(Mods.Alt)) mods.Add("alt");
            if (Mods.HasFlag(Mods.Shift)) mods.Add("shift");
            if (Mods.HasFlag(Mods.Super)) mods.Add("super");
            mods.Add(name);
            return string.Join('+', mods);
        }
    }

    private static Key? KeyFor(string name)
    {
        var lower = name.ToLowerInvariant();

        if (lower.Length == 1)
        {
            var c = lower[0];
            return c switch
            {
                >= 'a' and <= 'z' => Key.A + (c - 'a'),
                >= '0' and <= '9' => Key.Digit0 + (c - '0'),
                '/' => Key.Slash,
                '\\' => Key.Backslash,
                '[' => Key.BracketLeft,
                ']' => Key.BracketRight,
                ';' => Key.Semicolon,
                '\'' => Key.Quote,
                ',' => Key.Comma,
                '.' => Key.Period,
                '-' => Key.Minus,
                '=' => Key.Equal,
                '`' => Key.Backquote,
                _ => null,
            };
        }

        if (lower.Length is 2 or 3 && lower[0] == 'f' && int.TryParse(lower[1..], out var f) && f is >= 1 and <= 12)
        {
            return Key.F1 + (f - 1);
        }

        return lower switch
        {
            "enter" or "return" => Key.Enter,
            "tab" => Key.Tab,
            "esc" or "escape" => Key.Escape,
            "space" => Key.Space,
            "backspace" => Key.Backspace,
            "left" or "arrowleft" => Key.ArrowLeft,
            "right" or "arrowright" => Key.ArrowRight,
            "up" or "arrowup" => Key.ArrowUp,
            "down" or "arrowdown" => Key.ArrowDown,
            "home" => Key.Home,
            "end" => Key.End,
            "pageup" or "pgup" => Key.PageUp,
            "pagedown" or "pgdn" => Key.PageDown,
            "delete" or "del" => Key.Delete,
            "insert" or "ins" => Key.Insert,
            _ => null,
        };
    }
}
