using System.Text;

namespace Fleet.Shared.Evidence;

public static class VerifyOutput
{
    public const int MaxLines = 40;

    public const int MaxBytes = 4096;

    public static string Tail(string output)
    {
        var text = output.ReplaceLineEndings("\n");
        var lines = text.Split('\n');
        var count = lines.Length > 1 && lines[^1].Length == 0 ? lines.Length - 1 : lines.Length;

        if (count <= MaxLines && Encoding.UTF8.GetByteCount(text) <= MaxBytes)
        {
            return text;
        }

        var kept = string.Join('\n', lines[Math.Max(0, count - MaxLines)..count]);
        var bytes = Encoding.UTF8.GetBytes(kept);

        if (bytes.Length <= MaxBytes)
        {
            return kept;
        }

        var start = bytes.Length - MaxBytes;

        while (start < bytes.Length && (bytes[start] & 0xC0) == 0x80)
        {
            start++;
        }

        return Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
    }
}
