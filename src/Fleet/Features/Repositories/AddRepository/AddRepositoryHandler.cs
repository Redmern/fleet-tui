using Fleet.Features.Repositories.AddRepository.Enums;
using Fleet.Features.Repositories.AddRepository.Models;
using Fleet.Ports.Git;
using Fleet.Ports.Git.Models;
using Fleet.Shared.Results;

namespace Fleet.Features.Repositories.AddRepository;

public sealed class AddRepositoryHandler(IGitRunner git)
{
    public async Task<Result<Repository>> HandleAsync(
        AddRepositoryCommand command, CancellationToken ct = default)
    {
        var name = command.Name.Trim();
        var branch = command.DefaultBranch.Trim();
        var url = command.Url?.Trim() ?? string.Empty;

        if (name.Length == 0)
        {
            return Fail("A repository name is required.");
        }

        if (branch.Length == 0)
        {
            return Fail("A default branch is required.");
        }

        if (command.Kind == AddRepositoryKind.CloneUrl && url.Length == 0)
        {
            return Fail("A URL is required to clone a repository.");
        }

        if (!Directory.Exists(command.ProjectRoot))
        {
            return Fail($"{command.ProjectRoot} does not exist.");
        }

        var bare = Path.Combine(command.ProjectRoot, name);
        if (Directory.Exists(bare))
        {
            return Fail($"{bare} already exists.");
        }

        try
        {
            return command.Kind == AddRepositoryKind.CreateNew
                ? await CreateNewAsync(command.ProjectRoot, bare, name, branch, ct)
                    .ConfigureAwait(false)
                : await CloneAsync(command.ProjectRoot, bare, name, url, branch, ct)
                    .ConfigureAwait(false);
        }
        catch (GitFailedException e)
        {
            return Fail(e.Message);
        }
    }

    private async Task<Result<Repository>> CreateNewAsync(
        string projectRoot, string bare, string name, string branch, CancellationToken ct)
    {
        await Run(projectRoot, ["init", "--bare", "--initial-branch", branch, name], ct)
            .ConfigureAwait(false);

        await SeedInitialCommitAsync(bare, branch, ct).ConfigureAwait(false);
        await AddWorktreeAsync(bare, branch, ct).ConfigureAwait(false);

        return Result<Repository>.Ok(new Repository(name, bare, branch));
    }

    private async Task<Result<Repository>> CloneAsync(
        string projectRoot, string bare, string name, string url, string branch,
        CancellationToken ct)
    {
        await Run(projectRoot, ["clone", "--bare", url, name], ct).ConfigureAwait(false);

        var effective = branch;
        if (effective.Length == 0)
        {
            var head = await git
                .RunAsync(bare, ["symbolic-ref", "--short", "HEAD"], null, ct)
                .ConfigureAwait(false);

            effective = head.Ok && head.Out.Length > 0 ? head.Out : "main";
        }

        await AddWorktreeAsync(bare, effective, ct).ConfigureAwait(false);

        return Result<Repository>.Ok(new Repository(name, bare, effective));
    }

    private async Task AddWorktreeAsync(string bare, string branch, CancellationToken ct)
    {
        var dir = Path.Combine(bare, BranchSlug.Of(branch));
        if (Directory.Exists(dir))
        {
            return;
        }

        await Run(bare, ["worktree", "add", dir, branch], ct).ConfigureAwait(false);
    }

    private async Task SeedInitialCommitAsync(string bare, string branch, CancellationToken ct)
    {
        var tree = await git.RunAsync(bare, ["mktree"], stdin: string.Empty, ct)
            .ConfigureAwait(false);

        if (!tree.Ok)
        {
            throw new GitFailedException("mktree", tree);
        }

        var commit = await git
            .RunAsync(bare, ["commit-tree", tree.Out, "-m", "Initial commit"], null, ct)
            .ConfigureAwait(false);

        if (!commit.Ok)
        {
            throw new GitFailedException("commit-tree", commit);
        }

        await Run(bare, ["update-ref", $"refs/heads/{branch}", commit.Out], ct)
            .ConfigureAwait(false);

        await Run(bare, ["symbolic-ref", "HEAD", $"refs/heads/{branch}"], ct)
            .ConfigureAwait(false);
    }

    private async Task Run(string dir, string[] args, CancellationToken ct)
    {
        var r = await git.RunAsync(dir, args, null, ct).ConfigureAwait(false);

        if (!r.Ok)
        {
            throw new GitFailedException(args[0], r);
        }
    }

    private static Result<Repository> Fail(string reason) => Result<Repository>.Fail(reason);

    private sealed class GitFailedException(string verb, GitResult result)
        : Exception($"git {verb}: {result.Message}");
}
