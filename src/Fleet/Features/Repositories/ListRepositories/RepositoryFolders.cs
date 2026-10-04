using Fleet.Features.Repositories.ListRepositories.Models;

namespace Fleet.Features.Repositories.ListRepositories;

public static class RepositoryFolders
{
    private const string FallbackBranch = "main";
    private const string HeadPrefix = "ref: refs/heads/";

    public static IReadOnlyList<RepositorySummary> Skim(string projectRoot)
    {
        try
        {
            if (!Directory.Exists(projectRoot))
            {
                return [];
            }

            return
            [
                .. Directory.EnumerateDirectories(projectRoot)
                    .Select(dir => (Dir: dir, GitDir: BareGitDir(dir)))
                    .Where(r => r.GitDir is not null)
                    .Select(r => new RepositorySummary(Path.GetFileName(r.Dir), r.Dir, HeadBranch(r.GitDir!)))
                    .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase),
            ];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string? BareGitDir(string dir)
    {
        var nested = Path.Combine(dir, ".git");

        return IsBare(nested) ? nested : IsBare(dir) ? dir : null;
    }

    private static bool IsBare(string gitDir)
    {
        var config = Path.Combine(gitDir, "config");

        return File.Exists(Path.Combine(gitDir, "HEAD"))
            && Directory.Exists(Path.Combine(gitDir, "objects"))
            && Directory.Exists(Path.Combine(gitDir, "refs"))
            && File.Exists(config)
            && File.ReadLines(config).Any(SaysBare);
    }

    private static bool SaysBare(string line)
    {
        var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);

        return parts.Length == 2
            && parts[0].Equals("bare", StringComparison.OrdinalIgnoreCase)
            && parts[1].Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private static string HeadBranch(string gitDir)
    {
        var head = File.ReadAllText(Path.Combine(gitDir, "HEAD")).Trim();

        return head.StartsWith(HeadPrefix, StringComparison.Ordinal) && head.Length > HeadPrefix.Length
            ? head[HeadPrefix.Length..]
            : FallbackBranch;
    }
}
