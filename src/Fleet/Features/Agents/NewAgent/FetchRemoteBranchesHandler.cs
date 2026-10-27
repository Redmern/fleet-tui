using Fleet.Features.Agents.NewAgent.Models;
using Fleet.Ports.Git;
using Fleet.Shared.Results;

namespace Fleet.Features.Agents.NewAgent;

public sealed class FetchRemoteBranchesHandler(IGitRunner git, TimeSpan? timeout = null)
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    public async Task<Result<IReadOnlyList<BranchChoice>>> HandleAsync(
        string repositoryDirectory, CancellationToken ct = default)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout ?? DefaultTimeout);

        try
        {
            var fetched = await git
                .RunAsync(repositoryDirectory, ["fetch", "--prune", "origin"], null, limit.Token)
                .ConfigureAwait(false);

            if (!fetched.Ok)
            {
                return Result<IReadOnlyList<BranchChoice>>.Fail($"offline: {fetched.Message}");
            }

            var listed = await new ListBranchesHandler(git)
                .HandleAsync(repositoryDirectory, limit.Token)
                .ConfigureAwait(false);

            return Result<IReadOnlyList<BranchChoice>>.Ok(listed);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result<IReadOnlyList<BranchChoice>>.Fail("offline: fetch timed out");
        }
    }
}
