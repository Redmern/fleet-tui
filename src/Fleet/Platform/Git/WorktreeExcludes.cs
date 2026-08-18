using Fleet.Ports.Git;

namespace Fleet.Platform.Git;

public static class WorktreeExcludes
{
    private const string Header = "# fleet";

    public static async Task EnsureLocallyExcludedAsync(
        IGitRunner git,
        string worktree,
        IReadOnlyList<string> patterns,
        CancellationToken ct = default)
    {
        try
        {
            var common = await git
                .RunAsync(worktree, ["rev-parse", "--git-common-dir"], null, ct)
                .ConfigureAwait(false);

            if (!common.Ok || common.Out.Length == 0)
            {
                return;
            }

            var info = Path.Combine(Path.GetFullPath(common.Out, worktree), "info");
            var exclude = Path.Combine(info, "exclude");

            var existing = File.Exists(exclude) ? File.ReadAllLines(exclude) : [];
            var have = new HashSet<string>(existing, StringComparer.Ordinal);

            var missing = patterns.Where(p => !have.Contains(p)).ToList();

            if (missing.Count == 0)
            {
                return;
            }

            Directory.CreateDirectory(info);

            var block = new List<string>();

            if (existing.Length > 0 && existing[^1].Trim().Length > 0)
            {
                block.Add(string.Empty);
            }

            if (!have.Contains(Header))
            {
                block.Add(Header);
            }

            block.AddRange(missing);

            File.AppendAllLines(exclude, block);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
