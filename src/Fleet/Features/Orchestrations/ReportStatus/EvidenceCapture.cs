using Fleet.Features.Orchestrations.ReportStatus.Models;
using Fleet.Ports.Git;
using Fleet.Shared.Evidence;

namespace Fleet.Features.Orchestrations.ReportStatus;

public sealed class EvidenceCapture(IGitRunner git)
{
    public async Task<ReportEvidence> CaptureAsync(
        string worktree, string baseRef, VerifyReport? verify, CancellationToken ct = default)
    {
        var top = await git.RunAsync(worktree, ["rev-parse", "--show-toplevel"], ct: ct).ConfigureAwait(false);
        var head = await git.RunAsync(worktree, ["rev-parse", "HEAD"], ct: ct).ConfigureAwait(false);
        var status = await git.RunAsync(worktree, ["status", "--porcelain"], ct: ct).ConfigureAwait(false);
        var mergeBase = baseRef.Length == 0
            ? null
            : await git.RunAsync(worktree, ["merge-base", "HEAD", baseRef], ct: ct).ConfigureAwait(false);

        return new ReportEvidence(
            top.Ok && top.Out.Length > 0 ? top.Out : worktree,
            baseRef,
            mergeBase is { Ok: true } ? mergeBase.Out : string.Empty,
            head.Ok ? head.Out : string.Empty,
            status.Ok && Changed(status.StdOut),
            verify?.Command ?? string.Empty,
            verify?.ExitCode,
            VerifyOutput.Tail(verify?.Output ?? string.Empty));
    }

    private static bool Changed(string porcelain) =>
        porcelain
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Any(line => line.Length > 3
                         && !line[3..].Trim().Replace('\\', '/').StartsWith(".fleet/", StringComparison.Ordinal));
}
