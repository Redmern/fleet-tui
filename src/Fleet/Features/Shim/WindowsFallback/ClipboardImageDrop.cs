using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;

namespace Fleet.Features.Shim.WindowsFallback;

public static class ClipboardImageDrop
{
    public const string Prefix = "clipboard-";

    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(1);

    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private const string ShellSpecial = " $`&;'\"()<>|*?[]{}\\";

    private const string PowerShellQuotes = "'‘’‚‛";

    private static readonly ConcurrentDictionary<string, string> Fallbacks = new(StringComparer.Ordinal);

    public static string Folder => Path.Combine(Path.GetTempPath(), "fleet-clipboard");

    public static string? Drop(byte[] png, string folder, DateTimeOffset now, bool windowsHost, Action<string> log)
    {
        try
        {
            return Typed(Save(png, SafeFolder(folder, log), now), windowsHost);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log($"clipboard image: cannot save it in {folder}: {e.Message}");
            return null;
        }
    }

    public static string SafeFolder(string folder, Action<string> log)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(folder);
            return folder;
        }

        if (Fallbacks.TryGetValue(folder, out var kept) && Directory.Exists(kept))
        {
            return kept;
        }

        try
        {
            if (!Path.Exists(folder) && new FileInfo(folder).LinkTarget is null)
            {
                Directory.CreateDirectory(folder, OwnerOnly);
            }
        }
        catch (IOException)
        {
        }

        if (Private(folder))
        {
            return folder;
        }

        var fresh = Directory.CreateTempSubdirectory("fleet-clipboard-").FullName;
        log($"clipboard image: {folder} is not a private folder of this user; saving in {fresh}");
        return Fallbacks[folder] = fresh;
    }

    public static string Save(byte[] png, string folder, DateTimeOffset now)
    {
        Prune(folder, now);

        var stamp = now.UtcDateTime.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        while (true)
        {
            var path = Path.Combine(folder, $"{Prefix}{stamp}-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8))}.png");
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

    public static string Typed(string path, bool windowsHost)
    {
        if (!path.Any(c => char.IsWhiteSpace(c) || ShellSpecial.Contains(c) || (windowsHost && PowerShellQuotes.Contains(c))))
        {
            return path;
        }

        return windowsHost
            ? "'" + string.Concat(path.Select(c => PowerShellQuotes.Contains(c) ? $"{c}{c}" : c.ToString())) + "'"
            : "'" + path.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    }

    private static bool Private(string folder)
    {
        try
        {
            var info = new DirectoryInfo(folder);
            if (!info.Exists || info.LinkTarget is not null || info.UnixFileMode != OwnerOnly)
            {
                return false;
            }

            _ = info.EnumerateFileSystemInfos().FirstOrDefault();
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

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
