using Fleet.Platform.Mux.Embedded.Native;
using Fleet.Platform.Mux.Embedded.Protocol;

namespace Fleet.Platform.Mux.Embedded.Input;

public sealed class CopyMode : IStickyMode
{
    public const string Badge = "copy: hjkl move, v select, y copy, q quit";

    public bool Active { get; private set; }

    public void Enter() => Active = true;

    public CommandMessage? OnKey(Key key, Mods mods, string? text = null)
    {
        if ((mods & Mods.Ctrl) != 0)
        {
            return key switch
            {
                Key.U => Step("halfup"),
                Key.D => Step("halfdown"),
                Key.C => Step("exit"),
                _ => null,
            };
        }

        var special = key switch
        {
            Key.ArrowUp => "up",
            Key.ArrowDown => "down",
            Key.ArrowLeft => "left",
            Key.ArrowRight => "right",
            Key.PageUp => "pageup",
            Key.PageDown => "pagedown",
            Key.Home => "start",
            Key.End => "end",
            Key.Escape => "exit",
            Key.Enter => "yank",
            _ => null,
        };

        if (special is not null)
        {
            return Step(special);
        }

        return text is { Length: 1 } ? OnChar(text[0]) : null;
    }

    public int OnBytes(ReadOnlySpan<byte> bytes, out CommandMessage? command)
    {
        if (bytes.Length >= 3 && bytes[0] == 0x1b && bytes[1] == '[')
        {
            var arrow = (char)bytes[2] switch
            {
                'A' => "up",
                'B' => "down",
                'C' => "right",
                'D' => "left",
                'H' => "start",
                'F' => "end",
                _ => null,
            };

            if (arrow is not null)
            {
                command = Step(arrow);
                return 3;
            }

            if (bytes.Length >= 4 && bytes[3] == '~')
            {
                var named = (char)bytes[2] switch
                {
                    '5' => "pageup",
                    '6' => "pagedown",
                    '1' or '7' => "start",
                    '4' or '8' => "end",
                    _ => null,
                };

                if (named is not null)
                {
                    command = Step(named);
                    return 4;
                }
            }
        }

        command = bytes[0] switch
        {
            0x15 => Step("halfup"),
            0x04 => Step("halfdown"),
            0x03 or 0x1b => Step("exit"),
            (byte)'\r' => Step("yank"),
            _ => OnChar((char)bytes[0]),
        };

        return 1;
    }

    private CommandMessage? OnChar(char c) => c switch
    {
        'h' => Step("left"),
        'j' => Step("down"),
        'k' => Step("up"),
        'l' => Step("right"),
        '0' or '^' => Step("start"),
        '$' => Step("end"),
        'g' => Step("top"),
        'G' => Step("bottom"),
        'v' or ' ' => Step("select"),
        'y' => Step("yank"),
        'q' => Step("exit"),
        _ => null,
    };

    private CommandMessage Step(string step)
    {
        if (step is "yank" or "exit")
        {
            Active = false;
        }

        return new CommandMessage { Name = "copy", Arg = step };
    }
}