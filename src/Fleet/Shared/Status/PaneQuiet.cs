using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Fleet.Shared.Status.Models;

namespace Fleet.Shared.Status;

public static class PaneQuiet
{
    public static readonly TimeSpan QuietAfter = TimeSpan.FromSeconds(120);

    public const string FileName = "pane.mark";

    public static string Hash(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Normalized(text))));

    public static PaneMark Next(PaneMark? previous, string text, DateTime now)
    {
        var hash = Hash(text);

        return previous is not null && previous.Hash == hash ? previous : new PaneMark(hash, now);
    }

    public static bool Quiet(PaneMark mark, DateTime now, TimeSpan window) => now - mark.Since >= window;

    public static PaneMark Observe(string folder, string text, DateTime now)
    {
        var path = Path.Combine(folder, ".fleet", FileName);
        var mark = Next(Read(path), text, now);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, Format(mark));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }

        return mark;
    }

    private static PaneMark? Read(string path)
    {
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static string Format(PaneMark mark) =>
        $"{mark.Hash} {mark.Since.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)}";

    private static PaneMark? Parse(string text)
    {
        var parts = text.Trim().Split(' ');

        return parts.Length == 2
               && DateTime.TryParse(
                   parts[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var since)
            ? new PaneMark(parts[0], since)
            : null;
    }

    private static string Normalized(string text) =>
        string.Join('\n', text.ReplaceLineEndings("\n").Split('\n').Select(l => l.TrimEnd())).TrimEnd();
}
