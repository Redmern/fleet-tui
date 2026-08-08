using System.Text;

namespace RoyalPtySpike;

public static class SpikeLog
{
    private static readonly object Gate = new();

    public static string Path { get; } =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ptyspike.log");

    public static void Start(string header)
    {
        lock (Gate)
        {
            File.WriteAllText(Path, $"=== {header} ==={Environment.NewLine}", Encoding.UTF8);
        }
    }

    public static void Write(string line)
    {
        lock (Gate)
        {
            try
            {
                File.AppendAllText(
                    Path,
                    $"{DateTime.Now:HH:mm:ss.fff}  {line}{Environment.NewLine}",
                    Encoding.UTF8);
            }
            catch (IOException)
            {
            }
        }
    }
}
