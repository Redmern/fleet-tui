using System.Text;
using Fleet.Platform.Mux.Embedded.Host;

namespace Fleet.Platform.Mux.Embedded.Input;

public static class PasteBurst
{
    public const int QuietMs = 15;

    private const int MinimumWithoutNewline = 8;
    private const uint RightAlt = 0x0001;
    private const uint LeftAlt = 0x0002;
    private const uint RightCtrl = 0x0004;
    private const uint LeftCtrl = 0x0008;

    public static bool Starts(ReadOnlySpan<WindowsConsole.InputRecord> records)
    {
        var text = 0;

        foreach (var record in records)
        {
            if (record.EventType != WindowsConsole.KeyEvent || record.Key.KeyDown == 0)
            {
                continue;
            }

            if (IsChord(record.Key.ControlKeyState))
            {
                return false;
            }

            if (IsText(record.Key.UnicodeChar))
            {
                text++;
            }
        }

        return text >= 2;
    }

    public static string? Paste(IReadOnlyList<WindowsConsole.InputRecord> records)
    {
        var text = new StringBuilder();

        foreach (var record in records)
        {
            if (record.EventType != WindowsConsole.KeyEvent || record.Key.KeyDown == 0)
            {
                continue;
            }

            if (IsChord(record.Key.ControlKeyState))
            {
                return null;
            }

            var c = record.Key.UnicodeChar;
            if (IsText(c) || char.IsSurrogate(c))
            {
                text.Append(c, Math.Max((int)record.Key.RepeatCount, 1));
            }
        }

        var normalized = text.ToString().Replace("\r\n", "\r", StringComparison.Ordinal).Replace('\n', '\r');
        var newline = normalized.Contains('\r', StringComparison.Ordinal);

        return newline || normalized.Length >= MinimumWithoutNewline ? normalized : null;
    }

    private static bool IsText(char c) => c >= 0x20 || c is '\r' or '\n' or '\t';

    private static bool IsChord(uint state)
    {
        var altGr = (state & RightAlt) != 0 && (state & LeftCtrl) != 0 && (state & RightCtrl) == 0;
        return !altGr && (state & (LeftCtrl | RightCtrl | LeftAlt | RightAlt)) != 0;
    }
}
