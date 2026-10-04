using System.Collections.Concurrent;
using Fleet.Features.Agents;
using Fleet.Features.Agents.ListAgents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Models;
using Fleet.Shared.Constants;
using Fleet.Shared.Status.Models;

namespace Fleet.Cli.Composition;

public sealed class PaneActivity(IMuxDriver mux)
{
    private const int AtOnce = 6;

    private readonly ConcurrentDictionary<string, string> _texts = new(StringComparer.OrdinalIgnoreCase);

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
            return agent with { Status = AgentActivity.For(AgentActivity.Confirmed(report.State, seen, report.Reason)) };
        }

        var text = await mux.GetTextAsync(pane.Id, ct).ConfigureAwait(false);
        _texts[agent.Worktree] = text;
        var live = AgentActivity.Classify(text);

        return live.Length == 0 && orchestrator ? agent : agent with { Status = live };
    }
}
