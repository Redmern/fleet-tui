using Fleet.Ports.Git;
using Fleet.Shared.Results;

namespace Fleet.Features.Repositories.SetDefaultBranch;

public sealed class SetDefaultBranchHandler(IGitRunner git)
{
    public async Task<Result> HandleAsync(
        string container, string branch, CancellationToken ct = default)
    {
        var known = await git
            .RunAsync(container, ["rev-parse", "--verify", "--quiet", $"refs/heads/{branch}"],
                null, ct)
            .ConfigureAwait(false);

        if (!known.Ok)
        {
            return Result.Fail($"'{branch}' is not a branch in this repository.");
        }

        var set = await git
            .RunAsync(container, ["symbolic-ref", "HEAD", $"refs/heads/{branch}"], null, ct)
            .ConfigureAwait(false);

        return set.Ok ? Result.Ok() : Result.Fail($"git symbolic-ref: {set.Message}");
    }
}
