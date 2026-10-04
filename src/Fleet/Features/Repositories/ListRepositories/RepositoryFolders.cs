using Fleet.Features.Repositories.ListRepositories.Enums;
using Fleet.Features.Repositories.ListRepositories.Models;

namespace Fleet.Features.Repositories.ListRepositories;

public static class RepositoryFolders
{
    public const string FallbackBranch = "main";
    private const string HeadPrefix = "ref: refs/heads/";

    public static IReadOnlyList<RepositorySummary> Skim(string projectRoot) =>
        [
            .. Probe(projectRoot)
                .Where(p => p.Kind == FolderKind.Bare)
                .Select(p => new RepositorySummary(Path.GetFileName(p.Directory), p.Directory, p.DefaultBranch))
                .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase),
        ];

    public static IReadOnlyList<FolderProbe> Probe(string projectRoot)
    {
        try
        {
            return Directory.Exists(projectRoot)
                ? [.. Directory.EnumerateDirectories(projectRoot).Select(ProbeFolder)]
                : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public static FolderProbe ProbeFolder(string dir)
    {
        try
        {
            var nested = Path.Combine(dir, ".git");
            var gitDir = Directory.Exists(nested) ? nested
                : File.Exists(Path.Combine(dir, "HEAD")) ? dir
                : null;

            if (gitDir is null)
            {
                return new FolderProbe(dir, FolderKind.Plain);
            }

            var config = Path.Combine(gitDir, "config");

            if (!File.Exists(Path.Combine(gitDir, "HEAD"))
                || !Directory.Exists(Path.Combine(gitDir, "objects"))
                || !Directory.Exists(Path.Combine(gitDir, "refs"))
                || !File.Exists(config))
            {
                return new FolderProbe(dir, FolderKind.Unclear);
            }

            return File.ReadLines(config).Select(BareSetting).LastOrDefault(b => b is not null) switch
            {
                true => new FolderProbe(dir, FolderKind.Bare, HeadBranch(gitDir)),
                false => new FolderProbe(dir, FolderKind.Plain),
                null => new FolderProbe(dir, FolderKind.Unclear),
            };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new FolderProbe(dir, FolderKind.Unclear);
        }
    }

    private static bool? BareSetting(string line)
    {
        var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);

        if (parts.Length != 2 || !parts[0].Equals("bare", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return parts[1].ToLowerInvariant() switch
        {
            "true" or "yes" or "on" or "1" => true,
            "false" or "no" or "off" or "0" => false,
            _ => null,
        };
    }

    private static string HeadBranch(string gitDir)
    {
        var head = File.ReadAllText(Path.Combine(gitDir, "HEAD")).Trim();

        return head.StartsWith(HeadPrefix, StringComparison.Ordinal) && head.Length > HeadPrefix.Length
            ? head[HeadPrefix.Length..]
            : FallbackBranch;
    }
}
