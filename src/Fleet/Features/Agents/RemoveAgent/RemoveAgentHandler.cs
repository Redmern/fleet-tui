using Fleet.Features.Agents.RemoveAgent.Models;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Git;
using Fleet.Ports.Mux;
using Fleet.Shared;
using Fleet.Shared.Constants;
using Fleet.Shared.Results;

namespace Fleet.Features.Agents.RemoveAgent;

public sealed class RemoveAgentHandler(
    IGitRunner git, IMuxDriver mux, IAgentStore store, IReadOnlyList<TimeSpan>? folderRetries = null)
{
    private const int Attempts = 4;

    private static readonly TimeSpan Backoff = TimeSpan.FromMilliseconds(400);

    private static readonly IReadOnlyList<TimeSpan> FolderPatience =
    [
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(3),
        TimeSpan.FromMilliseconds(3250),
    ];

    public async Task<WorktreeState> InspectAsync(
        AgentRecord agent, CancellationToken ct = default)
    {
        if (AgentHarness.IsOrchestrator(agent.Harness) || !IsWorktree(agent.Worktree))
        {
            return WorktreeState.Gone;
        }

        var status = await git
            .RunAsync(agent.Worktree, ["status", "--porcelain"], null, ct)
            .ConfigureAwait(false);

        return new WorktreeState(true, status.Ok ? WorktreeDirt.Parse(status.Out) : []);
    }

    public async Task<int?> UnpushedAsync(AgentRecord agent, CancellationToken ct = default)
    {
        if (AgentHarness.IsOrchestrator(agent.Harness) || !IsWorktree(agent.Worktree))
        {
            return 0;
        }

        string[] elsewhere = ["rev-list", "--count", "HEAD", "--not", "--remotes"];

        if (agent.BaseRef.Length > 0
            && await CountAsync(agent.Worktree, [.. elsewhere, agent.BaseRef], ct).ConfigureAwait(false)
                is { } beyondBase)
        {
            return beyondBase;
        }

        return await CountAsync(agent.Worktree, elsewhere, ct).ConfigureAwait(false);
    }

    private async Task<int?> CountAsync(string worktree, string[] args, CancellationToken ct)
    {
        var counted = await git.RunAsync(worktree, args, null, ct).ConfigureAwait(false);

        return counted.Ok && int.TryParse(counted.Out, out var count) ? count : null;
    }

    public async Task<Result> HandleAsync(
        string project,
        AgentRecord agent,
        bool deleteWorktree,
        CancellationToken ct = default)
    {
        var removal = await RemoveAsync(project, agent, deleteWorktree, ct).ConfigureAwait(false);

        return removal.Problem is null ? Result.Ok() : Result.Fail(removal.Problem);
    }

    public async Task<AgentRemoval> RemoveAsync(
        string project,
        AgentRecord agent,
        bool deleteWorktree,
        CancellationToken ct = default)
    {
        if (!store.Remove(project, agent.Worktree))
        {
            return AgentRemoval.Refused(
                $"{agent.Branch} was not removed: fleet could not update its session file. Try again.");
        }

        var unterminated = await StopAsync(agent, ct).ConfigureAwait(false);

        if (!deleteWorktree)
        {
            return Removed(unterminated);
        }

        if (AgentHarness.IsOrchestrator(agent.Harness))
        {
            return await DiscardFolderAsync(agent.Worktree, ct).ConfigureAwait(false)
                ? Removed(unterminated)
                : AgentRemoval.FolderKept(Locked(WorktreeLock.Kept(agent.Worktree), unterminated));
        }

        var failure = await DiscardWorktreeAsync(agent.Worktree, ct).ConfigureAwait(false);

        if (failure is null)
        {
            return Removed(unterminated);
        }

        store.Save(project, agent with { Open = false });

        return AgentRemoval.Refused(Locked(failure, unterminated));
    }

    private static AgentRemoval Removed(IReadOnlyList<string> unterminated) =>
        unterminated.Count == 0 ? AgentRemoval.Done : AgentRemoval.Warned(WorktreeLock.Unterminated(unterminated));

    private static string Locked(string problem, IReadOnlyList<string> unterminated) =>
        unterminated.Count == 0 ? problem : $"{problem} {WorktreeLock.LockedBy(unterminated)}";

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

    private async Task<bool> DiscardFolderAsync(string folder, CancellationToken ct)
    {
        Discard(folder);

        foreach (var wait in folderRetries ?? FolderPatience)
        {
            if (!Directory.Exists(folder))
            {
                break;
            }

            await Task.Delay(wait, ct).ConfigureAwait(false);
            Discard(folder);
        }

        return !Directory.Exists(folder);
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

    private async Task<IReadOnlyList<string>> StopAsync(AgentRecord agent, CancellationToken ct)
    {
        var panes = await mux.ListPanesAsync(ct).ConfigureAwait(false);
        var unterminated = new List<string>();

        foreach (var pane in panes.Where(p => PathKey.Same(p.Cwd, agent.Worktree)))
        {
            if (await mux.TerminatePaneAsync(pane.Id, ct).ConfigureAwait(false) is { } problem)
            {
                unterminated.Add(problem);
            }
        }

        return unterminated;
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
