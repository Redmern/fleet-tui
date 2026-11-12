using System.Collections.Concurrent;
using Fleet.Features.Agents;
using Fleet.Features.Agents.ListAgents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Models;
using Fleet.Shared.Constants;
using Fleet.Shared.Status.Models;

namespace Fleet.Cli.Composition;

public sealed class PaneActivity(IMuxDriver mux, Action<AgentRecord>? wentIdle = null)
{
    private const int AtOnce = 6;

    private readonly ConcurrentDictionary<string, string> _texts = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, string> _quiet = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> Texts => _texts;

    public IReadOnlyList<AgentRecord> For(
        IReadOnlyList<AgentRecord> agents, IReadOnlyList<Pane> panes, Func<AgentRecord, AgentReport?> hooked)
    {
        var live = new AgentRecord[agents.Count];

        Parallel.ForEachAsync(
                Enumerable.Range(0, agents.Count),
                new ParallelOptions { MaxDegreeOfParallelism = AtOnce },
                async (i, ct) => live[i] = await WithActivityAsync(agents[i], panes, hooked, ct).ConfigureAwait(false))
            .GetAwaiter()
            .GetResult();

        return live;
    }

    private async Task<AgentRecord> WithActivityAsync(
        AgentRecord agent, IReadOnlyList<Pane> panes, Func<AgentRecord, AgentReport?> hooked, CancellationToken ct)
    {
        var orchestrator = AgentHarness.IsOrchestrator(agent.Harness);

        if (orchestrator
            && OrchestrationStatus.Normalize(agent.Status)
                is OrchestrationStatus.Done or OrchestrationStatus.Failed)
        {
            return agent;
        }

        var pane = panes.FirstOrDefault(p => AgentPanes.Owns(p, agent) && !SubBrowse.Is(p));

        if (pane is null)
        {
            return agent;
        }

        if (hooked(agent) is { } report)
        {
            var seen = AgentActivity.NeedsPane(report.State)
                ? await mux.GetTextAsync(pane.Id, ct).ConfigureAwait(false)
                : string.Empty;
            _texts[agent.Worktree] = seen;
            return WithLive(agent, AgentActivity.For(AgentActivity.Confirmed(report.State, seen, report.Reason)), orchestrator);
        }

        var text = await mux.GetTextAsync(pane.Id, ct).ConfigureAwait(false);
        _texts[agent.Worktree] = text;
        var live = AgentActivity.WatchedByPane(agent) ? Quiet(agent, text) : AgentActivity.Classify(text);

        return WithLive(agent, live, orchestrator);
    }

    private string Quiet(AgentRecord agent, string text)
    {
        var live = AgentActivity.Quiet(agent.Worktree, text, DateTime.UtcNow);
        var before = _quiet.TryGetValue(agent.Worktree, out var seen) ? seen : string.Empty;
        _quiet[agent.Worktree] = live;

        if (before == AgentActivity.Working && live == AgentActivity.Idle && !Finished(agent))
        {
            wentIdle?.Invoke(agent);
        }

        return live;
    }

    private static AgentRecord WithLive(AgentRecord agent, string live, bool orchestrator) =>
        KeepsStored(agent, live, orchestrator) ? agent : agent with { Status = live };

    private static bool KeepsStored(AgentRecord agent, string live, bool orchestrator) =>
        (live.Length == 0 && orchestrator)
        || (live is "" or AgentActivity.Idle && Finished(agent));

    private static bool Finished(AgentRecord agent) =>
        agent.Status.Trim().ToLowerInvariant() is OrchestrationStatus.Done or OrchestrationStatus.Failed;
}
