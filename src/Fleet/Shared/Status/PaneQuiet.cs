using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Fleet.Shared.Status.Models;

namespace Fleet.Shared.Status;

public static class PaneQuiet
{
    public static readonly TimeSpan QuietAfter = TimeSpan.FromSeconds(120);

    public const string FileName = "pane.mark";

    public static string Hash(string text, IReadOnlyList<Regex>? volatilePatterns = null) =>
        Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(PaneSettled.Text(text, volatilePatterns))));

    public static PaneMark Next(
        PaneMark? previous, string text, DateTime now, IReadOnlyList<Regex>? volatilePatterns = null)
    {
        var hash = Hash(text, volatilePatterns);

        return previous is not null && previous.Hash == hash ? previous : new PaneMark(hash, now);
    }

    public static bool Quiet(PaneMark mark, DateTime now, TimeSpan window) => now - mark.Since >= window;

    public static PaneMark Observe(
        string folder, string text, DateTime now, IReadOnlyList<Regex>? volatilePatterns = null)
    {
        var path = Path.Combine(folder, ".fleet", FileName);
        var mark = Next(Read(path), text, now, volatilePatterns);

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

    public static void Forget(string folder)
    {
        try
        {
            File.Delete(Path.Combine(folder, ".fleet", FileName));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }
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

        return parts.Length >= 2
               && DateTime.TryParse(
                   parts[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var since)
            ? new PaneMark(parts[0], since)
            : null;
    }
}
