using System.Globalization;

namespace Fleet.Features.Shim.WindowsFallback;

public static class ClipboardImageDrop
{
    public const string Prefix = "clipboard-";

    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(1);

    public static string Folder => Path.Combine(Path.GetTempPath(), "fleet-clipboard");

    public static string? Drop(byte[] png, string folder, DateTimeOffset now, Action<string> log)
    {
        try
        {
            return Typed(Save(png, folder, now));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log($"clipboard image: cannot save it in {folder}: {e.Message}");
            return null;
        }
    }

    public static string Save(byte[] png, string folder, DateTimeOffset now)
    {
        Directory.CreateDirectory(folder);
        Prune(folder, now);

        var stamp = now.UtcDateTime.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        for (var n = 0; ; n++)
        {
            var path = Path.Combine(folder, n == 0 ? $"{Prefix}{stamp}.png" : $"{Prefix}{stamp}-{n}.png");
            try
            {
                using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
                file.Write(png);
                return path;
            }
            catch (IOException) when (File.Exists(path))
            {
            }
        }
    }

    public static string Typed(string path) => path.Any(char.IsWhiteSpace) ? $"\"{path}\"" : path;

    private static void Prune(string folder, DateTimeOffset now)
    {
        foreach (var old in Directory.EnumerateFiles(folder, $"{Prefix}*.png"))
        {
            try
            {
                if (now.UtcDateTime - File.GetLastWriteTimeUtc(old) > KeepFor)
                {
                    File.Delete(old);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
