namespace Fleet.Features.Repositories.RemoveRepository;

public static class RepositoryContents
{
    public static IReadOnlyList<string> Worktrees(string porcelain)
    {
        var paths = new List<string>();

        foreach (var line in porcelain.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("worktree ", StringComparison.Ordinal))
            {
                paths.Add(trimmed["worktree ".Length..]);
            }
        }

        return paths;
    }

    public static IReadOnlyList<string> Unpushed(string tracking)
    {
        var branches = new List<string>();

        foreach (var line in tracking.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Trim().Split('|');

            if (fields.Length < 3 || fields[0].Length == 0)
            {
                continue;
            }

            var upstream = fields[1].Trim();
            var track = fields[2];

            if (upstream.Length == 0
                || track.Contains("ahead", StringComparison.Ordinal)
                || track.Contains("gone", StringComparison.Ordinal))
            {
                branches.Add(fields[0]);
            }
        }

        return branches;
    }
}
