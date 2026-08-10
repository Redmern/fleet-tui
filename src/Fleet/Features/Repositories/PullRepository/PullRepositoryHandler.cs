using Fleet.Ports.Git;
using Fleet.Shared.Results;

namespace Fleet.Features.Repositories.PullRepository;

public sealed class PullRepositoryHandler(IGitRunner git)
{
    public async Task<Result<string>> HandleAsync(
        string container, string branch, CancellationToken ct = default)
    {
        if (!Directory.Exists(container))
        {
            return Result<string>.Fail($"{container} does not exist.");
        }

        var fetched = await git
            .RunAsync(container, ["fetch", "--prune", "origin"], null, ct)
            .ConfigureAwait(false);

        if (!fetched.Ok)
        {
            return Result<string>.Fail($"git fetch: {fetched.Message}");
        }

        var worktree = RepositoryWorktree.For(container, branch, Directory.Exists);

        if (worktree == container)
        {
            return Result<string>.Ok($"fetched; {branch} has no worktree to fast-forward.");
        }

        var merged = await git
            .RunAsync(worktree, ["merge", "--ff-only", $"origin/{branch}"], null, ct)
            .ConfigureAwait(false);

        if (!merged.Ok)
        {
            return Result<string>.Ok(
                $"fetched; {branch} could not fast-forward: {PullOutcome.Summarise(merged.Message)}");
        }

        return Result<string>.Ok(PullOutcome.Summarise(merged.Out));
    }
}
