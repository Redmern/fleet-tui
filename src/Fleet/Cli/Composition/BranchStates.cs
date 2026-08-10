using Fleet.Features.Agents.RemoveAgent;
using Fleet.Ports.Git;
using Fleet.Shared;

namespace Fleet.Cli.Composition;

public sealed class BranchStates(IGitRunner git)
{
    public BranchState For(string worktree, string? fallback = null)
    {
        if (!Directory.Exists(worktree))
        {
            return BranchState.Unknown;
        }

        var (behind, ahead) = Distance(worktree, fallback);

        var status = git
            .RunAsync(worktree, ["status", "--porcelain"])
            .GetAwaiter()
            .GetResult();

        return new BranchState(
            ahead, behind, status.Ok && WorktreeDirt.Parse(status.Out).Count > 0);
    }

    private (int Behind, int Ahead) Distance(string worktree, string? fallback)
    {
        var tracked = Count(worktree, "@{upstream}");

        if (tracked is not null)
        {
            return tracked.Value;
        }

        var head = git
            .RunAsync(worktree, ["rev-parse", "--abbrev-ref", "HEAD"])
            .GetAwaiter()
            .GetResult();

        if (!head.Ok || head.Out.Length == 0)
        {
            return (0, 0);
        }

        var remote = Count(worktree, $"origin/{head.Out}");

        if (remote is not null)
        {
            return remote.Value;
        }

        return fallback is { Length: > 0 } ? Count(worktree, fallback) ?? (0, 0) : (0, 0);
    }

    private (int Behind, int Ahead)? Count(string worktree, string against)
    {
        var counts = git
            .RunAsync(worktree, ["rev-list", "--left-right", "--count", $"{against}...HEAD"])
            .GetAwaiter()
            .GetResult();

        return counts.Ok ? Tally.Parse(counts.Out) : null;
    }
}
