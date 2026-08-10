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
    private const int Attempts = 4;

    private static readonly TimeSpan Backoff = TimeSpan.FromMilliseconds(400);

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
        string project,
        AgentRecord agent,
        bool deleteWorktree,
        CancellationToken ct = default)
    {
        await StopAsync(agent, ct).ConfigureAwait(false);

        if (deleteWorktree)
        {
            var failure = await DiscardWorktreeAsync(agent.Worktree, ct).ConfigureAwait(false);

            if (failure is not null)
            {
                return Result.Fail(failure);
            }
        }

        store.Remove(project, agent.Worktree);

        return Result.Ok();
    }

    private async Task<string?> DiscardWorktreeAsync(string worktree, CancellationToken ct)
    {
        var anchor = await AnchorAsync(worktree, ct).ConfigureAwait(false)
                     ?? Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(worktree));

        if (anchor is null)
        {
            return $"Could not find the repository owning {worktree}.";
        }

        for (var attempt = 0; attempt < Attempts && IsWorktree(worktree); attempt++)
        {
            var removed = await git
                .RunAsync(anchor, ["worktree", "remove", "--force", worktree], null, ct)
                .ConfigureAwait(false);

            if (removed.Ok || WorktreeLock.AlreadyUnregistered(removed.Message))
            {
                break;
            }

            if (!WorktreeLock.LooksBusy(removed.Message))
            {
                return $"git worktree remove: {removed.Message}";
            }

            await Task.Delay(Backoff, ct).ConfigureAwait(false);
        }

        for (var attempt = 0; attempt < Attempts && Directory.Exists(worktree); attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(Backoff, ct).ConfigureAwait(false);
            }

            Discard(worktree);
        }

        await git.RunAsync(anchor, ["worktree", "prune"], null, ct).ConfigureAwait(false);

        return Directory.Exists(worktree) ? WorktreeLock.Busy(worktree) : null;
    }

    private static void Discard(string directory)
    {
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
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
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
