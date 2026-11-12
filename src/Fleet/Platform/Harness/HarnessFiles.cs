using System.Security.Cryptography;
using System.Text;
using Fleet.Shared.Results;

namespace Fleet.Platform.Harness;

public static class HarnessFiles
{
    private static readonly Lock InThisProcess = new();

    private const int DeepestLinkChain = 40;

    private static readonly TimeSpan LockPatience = TimeSpan.FromSeconds(5);

    public static string LockFor(string path)
    {
        var full = Path.GetFullPath(path);
        var key = OperatingSystem.IsWindows() ? full.ToUpperInvariant() : full;
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32];

        return Path.Combine(Path.GetTempPath(), $"fleet-{Environment.UserName}", "locks", name + ".lock");
    }

    public static string Canonical(string folder) => Canonical(folder, 0) ?? Path.GetFullPath(folder);

    public static Result Edit(string path, Func<string?, Result<string>> change)
    {
        lock (InThisProcess)
        {
            using var acrossProcesses = Held(LockFor(path));

            return Changed(path, change);
        }
    }

    public static void WriteSkills(string folder, string skillsDir, IReadOnlyDictionary<string, string> skills)
    {
        foreach (var (name, content) in skills)
        {
            var path = Path.Combine(folder, skillsDir, name, "SKILL.md");
            var text = content.Replace("\r\n", "\n", StringComparison.Ordinal);

            try
            {
                if (File.Exists(path) && File.ReadAllText(path) == text)
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, text);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static Result Changed(string link, Func<string?, Result<string>> change)
    {
        string path;
        string? text;

        try
        {
            path = File.Exists(link)
                ? File.ResolveLinkTarget(link, returnFinalTarget: true)?.FullName ?? link
                : link;
            text = File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Result.Fail($"fleet could not read {link}: {e.Message}");
        }

        var changed = change(text);

        if (!changed.Succeeded)
        {
            return Result.Fail($"{link} was left untouched: {changed.Error}.");
        }

        if (changed.Value == text)
        {
            return Result.Ok();
        }

        var staged = $"{path}.{Guid.NewGuid():N}.fleet-tmp";

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(staged, changed.Value);
            File.Move(staged, path, overwrite: true);
            return Result.Ok();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Discard(staged);
            return Result.Fail($"fleet could not write {link}: {e.Message}");
        }
    }

    private static FileStream? Held(string lockFile)
    {
        var deadline = DateTime.UtcNow + LockPatience;

        while (true)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(lockFile)!);

                return new FileStream(lockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(20);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    private static void Discard(string staged)
    {
        try
        {
            File.Delete(staged);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string? Canonical(string folder, int followed)
    {
        var full = Path.GetFullPath(folder);
        var root = Path.GetPathRoot(full) ?? string.Empty;
        var current = root;

        foreach (var part in full[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            var resolved = Resolved(Path.Combine(current, part), followed);

            if (resolved is null)
            {
                return null;
            }

            current = resolved;
        }

        return current;
    }

    private static string? Resolved(string path, int followed)
    {
        if (followed >= DeepestLinkChain)
        {
            return null;
        }

        try
        {
            var target = new DirectoryInfo(path).ResolveLinkTarget(returnFinalTarget: true);

            return target is null ? path : Canonical(target.FullName, followed + 1);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return path;
        }
    }
}
