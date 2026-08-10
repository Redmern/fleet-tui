using Fleet.Features.Agents.NewAgent;
using Fleet.Features.Agents.RemoveAgent;
using Fleet.Ports.Git;
using Fleet.Ports.Git.Models;

namespace Fleet.Cli.Composition;

public sealed class BranchStates(IGitRunner git)
{
    public BranchState For(string worktree, string baseRef)
    {
        if (!Directory.Exists(worktree))
        {
            return BranchState.Unknown;
        }

        var counts = git
            .RunAsync(worktree, ["rev-list", "--left-right", "--count", $"HEAD...{baseRef}"])
            .GetAwaiter()
            .GetResult();

        var status = git
            .RunAsync(worktree, ["status", "--porcelain"])
            .GetAwaiter()
            .GetResult();

        return new BranchState(
            counts.Ok ? BaseRef.Ahead(counts.Out) : 0,
            status.Ok && WorktreeDirt.Parse(status.Out).Count > 0);
    }
}
