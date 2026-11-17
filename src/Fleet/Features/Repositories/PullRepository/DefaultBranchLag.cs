using Fleet.Ports.Git;

namespace Fleet.Features.Repositories.PullRepository;

public sealed class DefaultBranchLag(IGitRunner git)
{
    public async Task<string> DescribeAsync(
        string container, string branch, CancellationToken ct = default)
    {
        var remote = $"origin/{branch}";

        var counts = await git
            .RunAsync(
                container,
                ["rev-list", "--left-right", "--count", $"refs/remotes/{remote}...refs/heads/{branch}"],
                null,
                ct)
            .ConfigureAwait(false);

        var parts = counts.Out.Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries);

        if (!counts.Ok
            || parts.Length < 2
            || !int.TryParse(parts[0], out var behind)
            || !int.TryParse(parts[1], out var ahead))
        {
            return $"local {branch} cannot be compared with {remote} (fetch first)";
        }

        return behind == 0 && ahead == 0
            ? $"local {branch} is level with {remote}"
            : $"local {branch} is {behind} behind and {ahead} ahead of {remote}";
    }
}
