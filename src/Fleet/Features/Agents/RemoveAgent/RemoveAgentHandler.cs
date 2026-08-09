using Fleet.Features.Agents.RemoveAgent.Models;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Git;
using Fleet.Ports.Mux;
using Fleet.Shared;
using Fleet.Shared.Results;

namespace Fleet.Features.Agents.RemoveAgent;

public sealed class RemoveAgentHandler(IGitRunner git, IMuxDriver mux, IAgentStore store)
{
    public async Task<WorktreeState> InspectAsync(
        AgentRecord agent, CancellationToken ct = default)
    {
        if (!IsWorktree(agent.Worktree))
        {
            return WorktreeState.Gone;
        }

        var status = await git
            .RunAsync(agent.Worktree, ["status", "--porcelain"], null, ct)
            .ConfigureAwait(false);

        return new WorktreeState(true, status.Ok ? WorktreeDirt.Parse(status.Out) : []);
    }

    public async Task<Result> HandleAsync(
        string project, AgentRecord agent, CancellationToken ct = default)
    {
        await StopAsync(agent, ct).ConfigureAwait(false);

        if (IsWorktree(agent.Worktree))
        {
            var anchor = await AnchorAsync(agent.Worktree, ct).ConfigureAwait(false);

            if (anchor is null)
            {
                return Result.Fail($"Could not find the repository owning {agent.Worktree}.");
            }

            var removed = await git
                .RunAsync(anchor, ["worktree", "remove", "--force", agent.Worktree], null, ct)
                .ConfigureAwait(false);

            if (!removed.Ok)
            {
                return Result.Fail($"git worktree remove: {removed.Message}");
            }

            await git.RunAsync(anchor, ["worktree", "prune"], null, ct).ConfigureAwait(false);
        }

        store.Remove(project, agent.Worktree);

        return Result.Ok();
    }

    private async Task StopAsync(AgentRecord agent, CancellationToken ct)
    {
        var panes = await mux.ListPanesAsync(ct).ConfigureAwait(false);

        foreach (var pane in panes.Where(p => PathKey.Same(p.Cwd, agent.Worktree)))
        {
            await mux.KillPaneAsync(pane.Id, ct).ConfigureAwait(false);
        }
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

    private static bool IsWorktree(string directory) =>
        Directory.Exists(directory)
        && (Directory.Exists(Path.Combine(directory, ".git"))
            || File.Exists(Path.Combine(directory, ".git")));
}
