using Fleet.Ports.Agents.Models;
using Fleet.Ports.Git;
using Fleet.Shared;
using Fleet.Shared.Results;

namespace Fleet.Features.Agents.FinishAgent;

public sealed class FinishAgentHandler(IGitRunner git)
{
    public async Task<Result<string>> HandleAsync(
        AgentRecord agent, bool push, CancellationToken ct = default)
    {
        var baseBranch = LocalBase(agent);

        if (baseBranch.Length == 0)
        {
            return Result<string>.Fail("this agent has no base branch to merge into.");
        }

        if (string.Equals(baseBranch, agent.Branch, StringComparison.OrdinalIgnoreCase))
        {
            return Result<string>.Fail(
                $"{agent.Branch} works on the base itself; there is nothing to merge.");
        }

        var container = agent.RepositoryWasBare
            ? Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(agent.Worktree))!
            : agent.Worktree;

        var slugged = Path.Combine(container, BranchSlug.Of(baseBranch));
        var baseWorktree = Directory.Exists(slugged) ? slugged : container;

        if (PathKey.Same(baseWorktree, agent.Worktree)
            || (agent.RepositoryWasBare && PathKey.Same(baseWorktree, container)))
        {
            return Result<string>.Fail(
                $"{baseBranch} has no worktree to merge into. Open it first.");
        }

        var merged = await git
            .RunAsync(baseWorktree, ["merge", "--ff-only", agent.Branch], null, ct)
            .ConfigureAwait(false);

        var how = "fast-forwarded";

        if (!merged.Ok)
        {
            merged = await git
                .RunAsync(baseWorktree, ["merge", "--no-edit", agent.Branch], null, ct)
                .ConfigureAwait(false);

            how = "merged";

            if (!merged.Ok)
            {
                await git.RunAsync(baseWorktree, ["merge", "--abort"], null, ct)
                    .ConfigureAwait(false);

                return Result<string>.Fail(
                    $"could not merge {agent.Branch} into {baseBranch}: "
                    + $"{merged.Message} Resolve or rebase the agent first.");
            }
        }

        if (!push)
        {
            return Result<string>.Ok($"{how} {agent.Branch} into {baseBranch}.");
        }

        var pushed = await git
            .RunAsync(baseWorktree, ["push", "origin", baseBranch], null, ct)
            .ConfigureAwait(false);

        return pushed.Ok
            ? Result<string>.Ok($"{how} {agent.Branch} into {baseBranch} and pushed.")
            : Result<string>.Ok(
                $"{how} {agent.Branch} into {baseBranch}, but the push failed: {pushed.Message}");
    }

    public static string LocalBase(AgentRecord agent)
    {
        var raw = agent.BaseRef.Trim();

        return raw.StartsWith("origin/", StringComparison.OrdinalIgnoreCase)
            ? raw["origin/".Length..]
            : raw;
    }
}
