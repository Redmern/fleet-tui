using System.Text;
using Fleet.Platform.Mux.Embedded.Protocol;

namespace Fleet.Platform.Mux.Embedded.Input;

public sealed class SgrMouse
{
    public const string Enable = "\e[?1002h\e[?1006h";

    private const int MaxSequence = 32;

    private readonly List<byte> _pending = [];

    public IEnumerable<object> Feed(ReadOnlySpan<byte> data)
    {
        var items = new List<object>();
        var bytes = new List<byte>(_pending.Count + data.Length);
        bytes.AddRange(_pending);
        bytes.AddRange(data.ToArray());
        _pending.Clear();

        var plain = new List<byte>();
        var i = 0;

        while (i < bytes.Count)
        {
            if (bytes[i] != 0x1b)
            {
                plain.Add(bytes[i++]);
                continue;
            }

            var end = Scan(bytes, i, out var complete);

            if (!complete)
            {
                if (end < 0)
                {
                    plain.Add(bytes[i++]);
                    continue;
                }

                _pending.AddRange(bytes.Skip(i));
                break;
            }

            if (Parse(bytes, i, end) is { } mouse)
            {
                if (plain.Count > 0)
                {
                    items.Add(plain.ToArray());
                    plain.Clear();
                }

                items.Add(mouse);
            }
            else
            {
                plain.AddRange(bytes.Skip(i).Take(end - i + 1));
            }

            i = end + 1;
        }

        if (plain.Count > 0)
        {
            items.Add(plain.ToArray());
        }

        return items;
    }

    private static int Scan(List<byte> bytes, int start, out bool complete)
    {
        complete = false;
        var prefix = "\e[<"u8;

        for (var k = 0; k < prefix.Length; k++)
        {
            if (start + k >= bytes.Count || bytes[start + k] != prefix[k])
            {
                return -1;
            }
        }

        for (var j = start + prefix.Length; j < bytes.Count && j - start < MaxSequence; j++)
        {
            var b = bytes[j];
            if (b is (byte)'M' or (byte)'m')
            {
                complete = true;
                return j;
            }

            if (b is not ((>= (byte)'0' and <= (byte)'9') or (byte)';'))
            {
                return -1;
            }
        }

        return bytes.Count - start >= MaxSequence ? -1 : start;
    }

    private static MouseMessage? Parse(List<byte> bytes, int start, int end)
    {
        var body = Encoding.ASCII.GetString(bytes.Skip(start + 3).Take(end - start - 3).ToArray()).Split(';');
        if (body.Length != 3
            || !int.TryParse(body[0], out var code)
            || !int.TryParse(body[1], out var x)
            || !int.TryParse(body[2], out var y))
        {
            return null;
        }

        var release = bytes[end] == (byte)'m';
        var motion = (code & 32) != 0;
        var wheel = (code & 64) != 0;
        var low = code & 3;

        var button = wheel
            ? MouseButtons.WheelUp + low
            : low switch { 0 => MouseButtons.Left, 1 => MouseButtons.Middle, 2 => MouseButtons.Right, _ => MouseButtons.None };

        var mods = ((code & 4) != 0 ? 1 : 0) | ((code & 16) != 0 ? 2 : 0) | ((code & 8) != 0 ? 4 : 0);

        return new MouseMessage
        {
            X = x - 1,
            Y = y - 1,
            Button = button,
            Action = release ? MouseActions.Release : motion ? MouseActions.Motion : MouseActions.Press,
            Mods = mods,
            Held = !release && !wheel && button != MouseButtons.None,
        };
    }
}
