using System.Globalization;
using Fleet.Ports.Git;
using Fleet.Shared.Evidence;

namespace Fleet.Features.Orchestrations.Flags;

public sealed class ReportDriftProbe(IGitRunner git)
{
    private const int NotAnAncestor = 1;

    public async Task<ReportDrift?> MeasureAsync(string worktree, string reportedHead, CancellationToken ct = default)
    {
        var reported = reportedHead.Trim();

        if (reported.Length == 0 || !Directory.Exists(worktree))
        {
            return null;
        }

        var head = await git.RunAsync(worktree, ["rev-parse", "--verify", "--quiet", "HEAD"], null, ct).ConfigureAwait(false);

        if (!head.Ok)
        {
            return null;
        }

        var ancestor = await git
            .RunAsync(worktree, ["merge-base", "--is-ancestor", reported, "HEAD"], null, ct)
            .ConfigureAwait(false);

        if (!ancestor.Ok)
        {
            return ancestor.ExitCode == NotAnAncestor || !await KnownAsync(worktree, reported, ct).ConfigureAwait(false)
                ? ReportDrift.HistoryRewritten
                : null;
        }

        var counted = await git.RunAsync(worktree, ["rev-list", "--count", $"{reported}..HEAD"], null, ct).ConfigureAwait(false);

        return counted.Ok
               && int.TryParse(counted.Out, NumberStyles.None, CultureInfo.InvariantCulture, out var commits)
               && commits > 0
            ? new ReportDrift(commits, false)
            : null;
    }

    private async Task<bool> KnownAsync(string worktree, string reported, CancellationToken ct) =>
        (await git.RunAsync(worktree, ["cat-file", "-e", $"{reported}^{{commit}}"], null, ct).ConfigureAwait(false)).Ok;
}
