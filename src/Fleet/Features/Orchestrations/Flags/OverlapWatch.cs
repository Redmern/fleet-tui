using Fleet.Features.Orchestrations.Flags.Models;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Git;
using Fleet.Shared.Constants;

namespace Fleet.Features.Orchestrations.Flags;

public sealed class OverlapWatch(
    IAgentStore store,
    IGitRunner git,
    Func<AgentRecord, string?> ownerFolder,
    Func<AgentRecord, string, CancellationToken, Task<bool>> tellOwner)
{
    public async Task<int> CheckAsync(string project, string owner, CancellationToken ct = default)
    {
        var key = owner.Trim();

        if (key.Length == 0)
        {
            return 0;
        }

        var live = store.List(project)
            .Where(a => a.Open
                        && !AgentHarness.IsOrchestrator(a.Harness)
                        && string.Equals(a.Owner.Trim(), key, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (live.Count < 2)
        {
            return 0;
        }

        var changed = new List<ChangedFiles>();
        var agents = new Dictionary<string, AgentRecord>(StringComparer.OrdinalIgnoreCase);

        foreach (var agent in live)
        {
            if (await ChangedAsync(agent, ct).ConfigureAwait(false) is { Count: > 0 } files)
            {
                var entry = new ChangedFiles(agent.Repository, agent.Branch, files);
                changed.Add(entry);
                agents[entry.Name] = agent;
            }
        }

        if (changed.Count < 2 || ownerFolder(live[0]) is not { Length: > 0 } folder)
        {
            return 0;
        }

        using var held = await OverlapLedger.LockAsync(folder, ct).ConfigureAwait(false);

        if (held is null)
        {
            return 0;
        }

        var named = OverlapLedger.Read(folder);
        var sent = 0;

        foreach (var pair in Overlaps.Due(changed, named))
        {
            if (await tellOwner(agents[pair.First], Overlaps.Line(pair), ct).ConfigureAwait(false))
            {
                var before = named.GetValueOrDefault(pair.Key) ?? new HashSet<string>();
                OverlapLedger.Append(folder, pair.Key, pair.Files.Where(f => !before.Contains(f)));
                sent++;
            }
        }

        return sent;
    }

    private async Task<IReadOnlyCollection<string>?> ChangedAsync(AgentRecord agent, CancellationToken ct)
    {
        var baseRef = agent.BaseRef.Trim();

        if (baseRef.Length == 0 || !Directory.Exists(agent.Worktree))
        {
            return null;
        }

        var diff = await git
            .RunAsync(agent.Worktree, ["diff", "--name-only", "-z", $"{baseRef}...HEAD"], null, ct)
            .ConfigureAwait(false);

        return diff.Ok
            ? [.. diff.StdOut.Split('\0', StringSplitOptions.RemoveEmptyEntries).Where(f => !f.Contains('\n', StringComparison.Ordinal))]
            : null;
    }
}
