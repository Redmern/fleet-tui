using System.Text;

namespace Fleet.Platform.Uninstall;

public static class ShellRcLines
{
    public const string Mark = "# added by fleet installer";

    private static readonly byte[] MarkBytes = Encoding.UTF8.GetBytes(Mark);

    public static bool IsMarked(string path) =>
        File.Exists(path) && File.ReadAllBytes(path).AsSpan().IndexOf(MarkBytes) >= 0;

    public static void Strip(string path, bool deleteWhenEmpty)
    {
        if (!IsMarked(path))
        {
            return;
        }

        var kept = Unmarked(File.ReadAllBytes(path));

        if (deleteWhenEmpty && kept.Length == 0)
        {
            File.Delete(path);
            return;
        }

        File.WriteAllBytes(path, kept);
    }

    private static byte[] Unmarked(byte[] content)
    {
        using var kept = new MemoryStream(content.Length);
        var rest = content.AsSpan();

        while (!rest.IsEmpty)
        {
            var end = rest.IndexOf((byte)'\n');
            var line = end < 0 ? rest : rest[..(end + 1)];

            if (line.IndexOf(MarkBytes) < 0)
            {
                kept.Write(line);
            }

            rest = rest[line.Length..];
        }

        return kept.ToArray();
    }
}
