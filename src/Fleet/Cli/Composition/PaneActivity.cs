using System.Collections.Concurrent;
using Fleet.Features.Agents;
using Fleet.Features.Agents.ListAgents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Models;
using Fleet.Shared.Constants;
using Fleet.Shared.Harness;
using Fleet.Shared.Settings.Models;
using Fleet.Shared.Status;
using Fleet.Shared.Status.Models;

namespace Fleet.Cli.Composition;

public sealed class PaneActivity(
    IMuxDriver mux,
    Action<AgentRecord, TimeSpan>? wentIdle = null,
    TimeProvider? clock = null,
    Action<AgentRecord>? lost = null,
    Func<IReadOnlyList<Pane>, bool>? listingIsWhole = null,
    Func<SettingsConfig>? settings = null,
    Func<string, HarnessProfile?>? profiles = null)
{
    private const int AtOnce = 6;

    private const int MissedRefreshes = 2;

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    private readonly ConcurrentDictionary<string, string> _texts = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, string> _quiet = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, bool> _seen = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, int> _missed = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, IReadOnlyList<Pane>> _counted = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> Texts => _texts;

    public IReadOnlyList<AgentRecord> For(
        IReadOnlyList<AgentRecord> agents, IReadOnlyList<Pane> panes, Func<AgentRecord, AgentReport?> hooked)
    {
        var live = new AgentRecord[agents.Count];
        var countable = listingIsWhole?.Invoke(panes) ?? panes.Count > 0;
        var config = agents.Any(AgentActivity.WatchedByPane) && settings is not null
            ? settings()
            : SettingsConfig.Default;

        Parallel.ForEachAsync(
                Enumerable.Range(0, agents.Count),
                new ParallelOptions { MaxDegreeOfParallelism = AtOnce },
                async (i, ct) => live[i] = await WithActivityAsync(agents[i], panes, hooked, countable, config, ct).ConfigureAwait(false))
            .GetAwaiter()
            .GetResult();

        return live;
    }

    private async Task<AgentRecord> WithActivityAsync(
        AgentRecord agent, IReadOnlyList<Pane> panes, Func<AgentRecord, AgentReport?> hooked, bool countable,
        SettingsConfig config, CancellationToken ct)
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
            if (countable)
            {
                Missing(agent, panes);
            }

            return agent;
        }

        _seen[agent.Worktree] = true;
        _missed.TryRemove(agent.Worktree, out _);

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
        var live = AgentActivity.WatchedByPane(agent) ? Quiet(agent, text, config) : AgentActivity.Classify(text);

        return WithLive(agent, live, orchestrator);
    }

    private void Missing(AgentRecord agent, IReadOnlyList<Pane> panes)
    {
        if (!agent.Open
            || OrchestrationStatus.Normalize(agent.Status) != OrchestrationStatus.Working
            || !_seen.ContainsKey(agent.Worktree)
            || ReferenceEquals(_counted.GetValueOrDefault(agent.Worktree), panes))
        {
            return;
        }

        _counted[agent.Worktree] = panes;

        if (_missed.AddOrUpdate(agent.Worktree, 1, (_, n) => n + 1) == MissedRefreshes)
        {
            _seen.TryRemove(agent.Worktree, out _);
            _missed.TryRemove(agent.Worktree, out _);
            _counted.TryRemove(agent.Worktree, out _);
            lost?.Invoke(agent);
        }
    }

    private string Quiet(AgentRecord agent, string text, SettingsConfig config)
    {
        var rule = PaneIdleRule.For(config, (profiles ?? HarnessRegistry.Find)(agent.RunningHarness));
        var live = AgentActivity.Quiet(agent.Worktree, text, _clock.GetUtcNow().UtcDateTime, rule);
        var before = _quiet.TryGetValue(agent.Worktree, out var seen) ? seen : string.Empty;
        _quiet[agent.Worktree] = live;

        if (before == AgentActivity.Working && live == AgentActivity.Idle && !Finished(agent))
        {
            wentIdle?.Invoke(agent, rule.Window);
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
