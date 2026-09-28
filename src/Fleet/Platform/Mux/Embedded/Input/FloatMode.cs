using Fleet.Platform.Mux.Embedded.Native;
using Fleet.Platform.Mux.Embedded.Protocol;

namespace Fleet.Platform.Mux.Embedded.Input;

public sealed class FloatMode
{
    public const string Badge = "float: hjkl move, HJKL size, esc done";

    public bool Active { get; private set; }

    public void Enter() => Active = true;

    public CommandMessage? OnKey(Key key, Mods mods)
    {
        var direction = key switch
        {
            Key.H or Key.ArrowLeft => "left",
            Key.J or Key.ArrowDown => "down",
            Key.K or Key.ArrowUp => "up",
            Key.L or Key.ArrowRight => "right",
            _ => null,
        };

        if (direction is not null)
        {
            return Step((mods & Mods.Shift) != 0, direction);
        }

        if (key is Key.Escape or Key.Enter or Key.Q or Key.G)
        {
            Active = false;
        }

        return null;
    }

    public int OnBytes(ReadOnlySpan<byte> bytes, out CommandMessage? command)
    {
        command = null;

        if (bytes.Length >= 3 && bytes[0] == 0x1b && bytes[1] == '[')
        {
            if (Arrow(bytes[2]) is { } plain)
            {
                command = Step(false, plain);
                return 3;
            }

            if (bytes.Length >= 6 && bytes[2] == '1' && bytes[3] == ';' && bytes[4] == '2' && Arrow(bytes[5]) is { } shifted)
            {
                command = Step(true, shifted);
                return 6;
            }
        }

        var c = (char)bytes[0];
        var direction = char.ToLowerInvariant(c) switch
        {
            'h' => "left",
            'j' => "down",
            'k' => "up",
            'l' => "right",
            _ => null,
        };

        if (direction is not null)
        {
            command = Step(char.IsUpper(c), direction);
        }
        else if (c is '\e' or '\r' or 'q' or 'g')
        {
            Active = false;
        }

        return 1;
    }

    private static CommandMessage Step(bool resize, string direction) =>
        new() { Name = resize ? "float-size" : "float-move", Arg = direction };

    private static string? Arrow(byte b) => b switch
    {
        (byte)'A' => "up",
        (byte)'B' => "down",
        (byte)'C' => "right",
        (byte)'D' => "left",
        _ => null,
    };
}
