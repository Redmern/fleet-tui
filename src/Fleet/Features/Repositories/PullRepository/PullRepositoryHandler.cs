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
            return Result<string>.Ok(await MoveLocalBranchAsync(container, branch, ct).ConfigureAwait(false));
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

    private async Task<string> MoveLocalBranchAsync(string container, string branch, CancellationToken ct)
    {
        var remote = $"origin/{branch}";
        var target = await RevisionAsync(container, $"refs/remotes/{remote}", ct).ConfigureAwait(false);

        if (target.Length == 0)
        {
            return $"fetched; origin has no {branch}.";
        }

        var before = await RevisionAsync(container, $"refs/heads/{branch}", ct).ConfigureAwait(false);

        if (before == target)
        {
            return $"fetched; {branch} already up to date with {remote}.";
        }

        if (before.Length > 0)
        {
            var ancestor = await git
                .RunAsync(container, ["merge-base", "--is-ancestor", before, target], null, ct)
                .ConfigureAwait(false);

            if (!ancestor.Ok)
            {
                return $"fetched; {branch} left as is: not a fast-forward of {remote} "
                    + "(the local branch has commits origin lacks).";
            }
        }

        var moved = await git
            .RunAsync(container, ["fetch", "origin", $"{branch}:{branch}"], null, ct)
            .ConfigureAwait(false);

        if (!moved.Ok)
        {
            return $"fetched; {branch} left as is: {PullOutcome.Summarise(moved.Message)}";
        }

        if (before.Length == 0)
        {
            return $"fetched; created {branch} at {PullOutcome.Short(target)}.";
        }

        var count = await git
            .RunAsync(container, ["rev-list", "--count", $"{before}..{target}"], null, ct)
            .ConfigureAwait(false);

        return $"fetched; moved {branch} {PullOutcome.Short(before)}..{PullOutcome.Short(target)}"
            + $"{PullOutcome.Commits(count.Ok ? count.Out : string.Empty)}.";
    }

    private async Task<string> RevisionAsync(string container, string reference, CancellationToken ct)
    {
        var parsed = await git
            .RunAsync(container, ["rev-parse", "--verify", "--quiet", reference], null, ct)
            .ConfigureAwait(false);

        return parsed.Ok ? parsed.Out : string.Empty;
    }
}
