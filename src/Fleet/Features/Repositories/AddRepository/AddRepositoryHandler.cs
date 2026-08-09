using Fleet.Features.Repositories.AddRepository.Enums;
using Fleet.Features.Repositories.AddRepository.Models;
using Fleet.Ports.Git;
using Fleet.Ports.Git.Models;
using Fleet.Shared;
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

        var container = Path.Combine(command.ProjectRoot, name);
        if (Directory.Exists(container))
        {
            return Fail($"{container} already exists.");
        }

        var gitDir = Path.Combine(name, ".git");

        try
        {
            return command.Kind == AddRepositoryKind.CreateNew
                ? await CreateNewAsync(command.ProjectRoot, container, gitDir, name, branch, ct)
                    .ConfigureAwait(false)
                : await CloneAsync(command.ProjectRoot, container, gitDir, name, url, branch, ct)
                    .ConfigureAwait(false);
        }
        catch (GitFailedException e)
        {
            return Fail(e.Message);
        }
    }

    private async Task<Result<Repository>> CreateNewAsync(
        string projectRoot, string container, string gitDir, string name, string branch,
        CancellationToken ct)
    {
        await Run(projectRoot, ["init", "--bare", "--initial-branch", branch, gitDir], ct)
            .ConfigureAwait(false);

        await SeedInitialCommitAsync(container, branch, ct).ConfigureAwait(false);
        await AddWorktreeAsync(container, branch, ct).ConfigureAwait(false);

        return Result<Repository>.Ok(new Repository(name, container, branch));
    }

    private async Task<Result<Repository>> CloneAsync(
        string projectRoot, string container, string gitDir, string name, string url,
        string branch, CancellationToken ct)
    {
        await Run(projectRoot, ["clone", "--bare", url, gitDir], ct).ConfigureAwait(false);

        var effective = branch;
        if (effective.Length == 0)
        {
            var head = await git
                .RunAsync(container, ["symbolic-ref", "--short", "HEAD"], null, ct)
                .ConfigureAwait(false);

            effective = head.Ok && head.Out.Length > 0 ? head.Out : "main";
        }

        var known = await git
            .RunAsync(container, ["rev-parse", "--verify", "--quiet", $"refs/heads/{effective}"],
                null, ct)
            .ConfigureAwait(false);

        if (!known.Ok)
        {
            Discard(container);
            return Fail($"The cloned repository has no branch named '{effective}'.");
        }

        await Run(container, ["symbolic-ref", "HEAD", $"refs/heads/{effective}"], ct)
            .ConfigureAwait(false);

        await AddWorktreeAsync(container, effective, ct).ConfigureAwait(false);

        return Result<Repository>.Ok(new Repository(name, container, effective));
    }

    private async Task AddWorktreeAsync(string container, string branch, CancellationToken ct)
    {
        var slug = BranchSlug.Of(branch);

        if (Directory.Exists(Path.Combine(container, slug)))
        {
            return;
        }

        await Run(container, ["worktree", "add", slug, branch], ct).ConfigureAwait(false);
    }

    private async Task SeedInitialCommitAsync(
        string container, string branch, CancellationToken ct)
    {
        var tree = await git.RunAsync(container, ["mktree"], stdin: string.Empty, ct)
            .ConfigureAwait(false);

        if (!tree.Ok)
        {
            throw new GitFailedException("mktree", tree);
        }

        var commit = await git
            .RunAsync(container, ["commit-tree", tree.Out, "-m", "Initial commit"], null, ct)
            .ConfigureAwait(false);

        if (!commit.Ok)
        {
            throw new GitFailedException("commit-tree", commit);
        }

        await Run(container, ["update-ref", $"refs/heads/{branch}", commit.Out], ct)
            .ConfigureAwait(false);

        await Run(container, ["symbolic-ref", "HEAD", $"refs/heads/{branch}"], ct)
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

    private static void Discard(string container)
    {
        if (!Directory.Exists(container))
        {
            return;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(
                container, "*", SearchOption.AllDirectories))
            {
                var attributes = File.GetAttributes(file);

                if (attributes.HasFlag(FileAttributes.ReadOnly))
                {
                    File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
                }
            }

            Directory.Delete(container, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed class GitFailedException(string verb, GitResult result)
        : Exception($"git {verb}: {result.Message}");
}
