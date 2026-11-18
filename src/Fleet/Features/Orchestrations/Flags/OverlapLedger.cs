using System.Text;
using Fleet.Shared.Orchestrations;

namespace Fleet.Features.Orchestrations.Flags;

public static class OverlapLedger
{
    public const string FileName = "overlaps.txt";

    private const int LockAttempts = 100;

    private static readonly TimeSpan LockRetry = TimeSpan.FromMilliseconds(20);

    public static string PathIn(string ownerFolder) => Path.Combine(ownerFolder, OrchestrationPaths.Folder, FileName);

    public static Dictionary<string, IReadOnlySet<string>> Parse(string text)
    {
        var sent = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in text.Split('\n'))
        {
            var tab = line.IndexOf('\t', StringComparison.Ordinal);

            if (tab <= 0)
            {
                continue;
            }

            var key = line[..tab];

            if (!sent.TryGetValue(key, out var files))
            {
                files = new HashSet<string>(StringComparer.Ordinal);
                sent[key] = files;
            }

            files.Add(line[(tab + 1)..]);
        }

        return sent.ToDictionary(s => s.Key, IReadOnlySet<string> (s) => s.Value, StringComparer.OrdinalIgnoreCase);
    }

    public static string Lines(string key, IEnumerable<string> files)
    {
        var text = new StringBuilder();

        foreach (var file in files.Where(f => !f.Contains('\n', StringComparison.Ordinal)))
        {
            text.Append(key).Append('\t').Append(file).Append('\n');
        }

        return text.ToString();
    }

    public static async Task<IDisposable?> LockAsync(string ownerFolder, CancellationToken ct = default)
    {
        var path = PathIn(ownerFolder) + ".lock";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        for (var attempt = 1; attempt <= LockAttempts; attempt++)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (attempt < LockAttempts)
            {
                await Task.Delay(LockRetry, ct).ConfigureAwait(false);
            }
            catch (IOException)
            {
                return null;
            }
        }

        return null;
    }

    public static Dictionary<string, IReadOnlySet<string>> Read(string ownerFolder)
    {
        try
        {
            var path = PathIn(ownerFolder);

            return Parse(File.Exists(path) ? File.ReadAllText(path) : string.Empty);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Parse(string.Empty);
        }
    }

    public static void Append(string ownerFolder, string key, IEnumerable<string> files)
    {
        var path = PathIn(ownerFolder);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.AppendAllText(path, Lines(key, files));
    }
}
