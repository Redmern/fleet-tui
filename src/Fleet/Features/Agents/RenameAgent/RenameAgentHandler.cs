using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Git;
using Fleet.Shared.Results;

namespace Fleet.Features.Agents.RenameAgent;

public sealed class RenameAgentHandler(IGitRunner git, IAgentStore store)
{
    public async Task<Result<AgentRecord>> HandleAsync(
        string project, AgentRecord agent, string newBranch, CancellationToken ct = default)
    {
        var target = newBranch.Trim();

        if (target.Length == 0)
        {
            return Result<AgentRecord>.Fail("A branch name is required.");
        }

        if (string.Equals(target, agent.Branch, StringComparison.Ordinal))
        {
            return Result<AgentRecord>.Fail("That is already its name.");
        }

        var anchor = await AnchorAsync(agent.Worktree, ct).ConfigureAwait(false);

        if (anchor is null)
        {
            return Result<AgentRecord>.Fail("Could not find the repository for this agent.");
        }

        if (await ExistsAsync(anchor, $"refs/heads/{target}", ct).ConfigureAwait(false))
        {
            return Result<AgentRecord>.Fail($"A branch named '{target}' already exists.");
        }

        var renamed = await git
            .RunAsync(anchor, ["branch", "-m", agent.Branch, target], null, ct)
            .ConfigureAwait(false);

        if (!renamed.Ok)
        {
            return Result<AgentRecord>.Fail($"git branch -m: {renamed.Message}");
        }

        var updated = agent with { Branch = target };

        store.Save(project, updated);

        return Result<AgentRecord>.Ok(updated);
    }

    private async Task<string?> AnchorAsync(string worktree, CancellationToken ct)
    {
        var common = await git
            .RunAsync(worktree, ["rev-parse", "--git-common-dir"], null, ct)
            .ConfigureAwait(false);

        if (!common.Ok || common.Out.Length == 0)
        {
            return null;
        }

        var resolved = Path.GetFullPath(common.Out, worktree);

        return Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(resolved));
    }

    private async Task<bool> ExistsAsync(string anchor, string reference, CancellationToken ct) =>
        (await git.RunAsync(anchor, ["rev-parse", "--verify", "--quiet", reference], null, ct)
            .ConfigureAwait(false)).Ok;
}
