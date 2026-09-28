using System.Text;

namespace Fleet.Platform.Mux.Embedded.Input;

public sealed class ConPtyModes
{
    private static readonly (byte[] Seq, Action<ConPtyModes> Apply)[] Sequences =
    [
        (Encoding.ASCII.GetBytes("\e[?9001h"), m => m.Win32Input = true),
        (Encoding.ASCII.GetBytes("\e[?9001l"), m => m.Win32Input = false),
        (Encoding.ASCII.GetBytes("\e[?1004h"), m => m.FocusEvents = true),
        (Encoding.ASCII.GetBytes("\e[?1004l"), m => m.FocusEvents = false),
        (Encoding.ASCII.GetBytes("\e[?2004h"), m => m.BracketedPaste = true),
        (Encoding.ASCII.GetBytes("\e[?2004l"), m => m.BracketedPaste = false),
    ];

    private readonly byte[] _tail = new byte[7];
    private int _tailLength;

    public volatile bool Win32Input;
    public volatile bool FocusEvents;
    public volatile bool BracketedPaste;

    public void Feed(ReadOnlySpan<byte> data)
    {
        var joined = new byte[_tailLength + data.Length];
        _tail.AsSpan(0, _tailLength).CopyTo(joined);
        data.CopyTo(joined.AsSpan(_tailLength));

        var span = joined.AsSpan();
        var at = span.IndexOf((byte)0x1b);
        while (at >= 0)
        {
            foreach (var (seq, apply) in Sequences)
            {
                if (span[at..].StartsWith(seq))
                {
                    apply(this);
                }
            }

            var next = span[(at + 1)..].IndexOf((byte)0x1b);
            at = next < 0 ? -1 : at + 1 + next;
        }

        _tailLength = Math.Min(_tail.Length, joined.Length);
        joined.AsSpan(joined.Length - _tailLength).CopyTo(_tail);
    }

    public static byte[] Encode(Host.WindowsConsole.KeyEventRecord r) =>
        Encode(r.VirtualKeyCode, r.VirtualScanCode, r.UnicodeChar, r.KeyDown != 0, r.ControlKeyState, r.RepeatCount);

    public static byte[] Encode(int vk, int sc, int uc, bool down, uint state, int repeat) =>
        Encoding.ASCII.GetBytes($"\e[{vk};{sc};{uc};{(down ? 1 : 0)};{state};{Math.Max(repeat, 1)}_");
}
