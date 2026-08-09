using Fleet.Features.Repositories.RemoveRepository.Models;
using Fleet.Ports.Git;
using Fleet.Shared.Results;

namespace Fleet.Features.Repositories.RemoveRepository;

public sealed class RemoveRepositoryHandler(IGitRunner git)
{
    public async Task<RepositoryState> InspectAsync(
        string directory, CancellationToken ct = default)
    {
        if (!Directory.Exists(directory))
        {
            return RepositoryState.Gone;
        }

        var worktrees = await git
            .RunAsync(directory, ["worktree", "list", "--porcelain"], null, ct)
            .ConfigureAwait(false);

        var unpushed = await git
            .RunAsync(
                directory,
                ["for-each-ref", "--format=%(refname:short)|%(upstream)|%(upstream:track)", "refs/heads"],
                null,
                ct)
            .ConfigureAwait(false);

        return new RepositoryState(
            true,
            RepositoryContents.Worktrees(worktrees.Ok ? worktrees.Out : string.Empty),
            RepositoryContents.Unpushed(unpushed.Ok ? unpushed.Out : string.Empty));
    }

    public Result Handle(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return Result.Ok();
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(
                directory, "*", SearchOption.AllDirectories))
            {
                var attributes = File.GetAttributes(file);

                if (attributes.HasFlag(FileAttributes.ReadOnly))
                {
                    File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
                }
            }

            Directory.Delete(directory, recursive: true);

            return Result.Ok();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Result.Fail($"Could not delete {directory}: {e.Message}");
        }
    }
}
