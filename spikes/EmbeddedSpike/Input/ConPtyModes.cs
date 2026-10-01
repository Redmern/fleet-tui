using System.Text;

namespace EmbeddedSpike.Input;

// Watches the pane's output for the two private modes ConPTY itself asks its
// host for: 9001 (win32-input-mode) and 1004 (focus events). ConPTY sends both
// as the first bytes of every session. libghostty-vt ignores 9001, so the spike
// tracks it here.
internal sealed class ConPtyModes
{
    private static readonly (byte[] Seq, Action<ConPtyModes> Apply)[] Sequences =
    [
        (Encoding.ASCII.GetBytes("\e[?9001h"), m => m.Win32Input = true),
        (Encoding.ASCII.GetBytes("\e[?9001l"), m => m.Win32Input = false),
        (Encoding.ASCII.GetBytes("\e[?1004h"), m => m.FocusEvents = true),
        (Encoding.ASCII.GetBytes("\e[?1004l"), m => m.FocusEvents = false),
    ];

    private readonly byte[] _tail = new byte[7];
    private int _tailLength;

    public volatile bool Win32Input;
    public volatile bool FocusEvents;

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

    // ESC [ Vk ; Sc ; Uc ; Kd ; Cs ; Rc _ — the record exactly as the console
    // delivered it, which ConPTY turns back into the same INPUT_RECORD for the
    // child. Spec: microsoft/terminal doc/specs/#4999 - Improved keyboard handling.
    public static byte[] Encode(Host.WindowsConsole.KeyEventRecord r) =>
        Encoding.ASCII.GetBytes(
            $"\e[{r.VirtualKeyCode};{r.VirtualScanCode};{(int)r.UnicodeChar};{(r.KeyDown != 0 ? 1 : 0)};{r.ControlKeyState};{Math.Max((int)r.RepeatCount, 1)}_");
}
