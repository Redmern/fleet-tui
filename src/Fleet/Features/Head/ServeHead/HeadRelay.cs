using Fleet.Features.Head.ServeHead.Enums;
using Fleet.Features.Head.ServeHead.Models;
using Fleet.Ports.Mcp.Models;
using Fleet.Ports.Mux.Models;
using Fleet.Ports.Projects.Models;
using Fleet.Shared;
using Fleet.Shared.Messaging;
using Fleet.Shared.Settings.Enums;

namespace Fleet.Features.Head.ServeHead;

public sealed class HeadRelay(HeadDeps deps, HeadGate gate, HeadTiming timing)
{
    private readonly Lock _lock = new();

    private readonly Dictionary<string, Queue<string>> _queues = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, Task> _pumps = new(StringComparer.OrdinalIgnoreCase);

    private readonly SemaphoreSlim _typing = new(1, 1);

    public int Pending(string project)
    {
        lock (_lock)
        {
            return _queues.TryGetValue(project, out var queue) ? queue.Count : 0;
        }
    }

    public Task Drained(string project)
    {
        lock (_lock)
        {
            return _pumps.TryGetValue(project, out var pump) ? pump : Task.CompletedTask;
        }
    }

    public Task<McpResult> RelayAsync(Project project, string prompt, CancellationToken ct)
    {
        var trigger = Trigger(project);

        return DeliverAsync(
            project,
            HarnessTool.Dispatch,
            RelayText.Dispatch(prompt, trigger),
            PeerMessage.DispatchRequest(RelayText.Plain(prompt, trigger)),
            "relayed to",
            ct);
    }

    public async Task<McpResult> TellAsync(Project project, string prompt, CancellationToken ct)
    {
        var text = RelayText.Plain(prompt, Trigger(project));

        return text.Length == 0
            ? McpResult.Error($"'{HeadTools.Prompt}' has nothing left once the dispatch trigger is removed.")
            : await DeliverAsync(project, HarnessTool.TellAgent, text, text, "told", ct).ConfigureAwait(false);
    }

    private string Trigger(Project project) => deps.Settings.Load(project.Name).MergedOverDefaults().Trigger;

    private async Task<McpResult> DeliverAsync(
        Project project, HarnessTool tool, string text, string message, string verb, CancellationToken ct)
    {
        if (gate.Refused(project.Name, tool) is { } refused)
        {
            return McpResult.Error(refused);
        }

        var opened = false;

        if (!await deps.IsOpen(project, ct).ConfigureAwait(false))
        {
            if (await deps.EnsureOpen(project).ConfigureAwait(false) is { } failed)
            {
                return McpResult.Error($"could not open {project.Name}: {failed}");
            }

            opened = true;
        }

        var lead = opened ? $"opened {project.Name}; " : string.Empty;

        if (!opened && await InboxAsync(project, ct).ConfigureAwait(false) is { } open)
        {
            return await ViaMessageAsync(project, tool, message, open, lead, ct).ConfigureAwait(false);
        }

        var (pane, readiness) = await WaitForReadyAsync(
            project, opened ? timing.OpenTimeout : TimeSpan.Zero, ct).ConfigureAwait(false);

        if (opened && pane is null)
        {
            return McpResult.Error(
                $"opened {project.Name}, but its orchestrator pane did not appear; try again in a moment.");
        }

        if (await gate.CheckAsync(project.Name, tool, text, ct).ConfigureAwait(false)
            is { } denied)
        {
            return McpResult.Error(denied);
        }

        if (pane is { } target && readiness == Readiness.Ready && Pending(project.Name) == 0)
        {
            await TypeAsync(project.Name, target.Id, text, ct).ConfigureAwait(false);
            Log(project.Name, $"head {verb} the orchestrator: {text}");

            return McpResult.Ok($"{lead}{verb} {project.Name}'s orchestrator: {text}");
        }

        var waiting = Enqueue(project, text);
        Log(project.Name, $"head queued a prompt for the orchestrator ({Why(pane, readiness)})");

        return McpResult.Ok(
            $"{lead}{project.Name}'s orchestrator is {Why(pane, readiness)}, so the prompt is queued "
            + $"({waiting} waiting). fleet types it in as soon as that Claude is idle: {text}");
    }

    private async Task<string?> InboxAsync(Project project, CancellationToken ct)
    {
        if (deps.Inboxes is not { } inboxes || Pending(project.Name) > 0)
        {
            return null;
        }

        var panes = await deps.Mux.ListPanesAsync(ct).ConfigureAwait(false);

        return MainPane.Find(panes, project.Root, deps.DashPane(project.Name)) is null
            ? null
            : await inboxes.AddressAsync(project.Root, ct).ConfigureAwait(false);
    }

    private async Task<McpResult> ViaMessageAsync(
        Project project, HarnessTool tool, string message, string address, string lead, CancellationToken ct)
    {
        if (tool != HarnessTool.Dispatch
            && await gate.CheckAsync(project.Name, tool, message, ct).ConfigureAwait(false) is { } denied)
        {
            return McpResult.Error(denied);
        }

        Log(project.Name, $"head hands a prompt for the orchestrator to SendMessage ({address})");

        return McpResult.Ok(lead + PeerMessage.SendYourself($"{project.Name}'s orchestrator", address, message));
    }

    private int Enqueue(Project project, string text)
    {
        lock (_lock)
        {
            if (!_queues.TryGetValue(project.Name, out var queue))
            {
                queue = new Queue<string>();
                _queues[project.Name] = queue;
            }

            queue.Enqueue(text);

            if (!_pumps.TryGetValue(project.Name, out var pump) || pump.IsCompleted)
            {
                _pumps[project.Name] = Task.Run(() => PumpAsync(project));
            }

            return queue.Count;
        }
    }

    private async Task PumpAsync(Project project)
    {
        var deadline = DateTime.UtcNow + timing.QueueTimeout;

        while (true)
        {
            string? next;

            lock (_lock)
            {
                next = _queues.TryGetValue(project.Name, out var queue) && queue.Count > 0 ? queue.Peek() : null;
            }

            if (next is null)
            {
                return;
            }

            if (DateTime.UtcNow > deadline)
            {
                var dropped = Clear(project.Name);
                Log(project.Name, $"head gave up on {dropped} queued prompt(s): the orchestrator never went idle");
                return;
            }

            var (pane, readiness) = await ProbeAsync(project, CancellationToken.None).ConfigureAwait(false);

            if (pane is { } target && readiness == Readiness.Ready)
            {
                await TypeAsync(project.Name, target.Id, next, CancellationToken.None).ConfigureAwait(false);
                Log(project.Name, $"head delivered a queued prompt to the orchestrator: {next}");

                lock (_lock)
                {
                    _queues[project.Name].Dequeue();
                }

                deadline = DateTime.UtcNow + timing.QueueTimeout;
            }

            await Task.Delay(timing.Poll).ConfigureAwait(false);
        }
    }

    private int Clear(string project)
    {
        lock (_lock)
        {
            if (!_queues.TryGetValue(project, out var queue))
            {
                return 0;
            }

            var count = queue.Count;
            queue.Clear();
            return count;
        }
    }

    private async Task<(Pane? Pane, Readiness Readiness)> WaitForReadyAsync(
        Project project, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (true)
        {
            var probe = await ProbeAsync(project, ct).ConfigureAwait(false);

            if (probe.Readiness == Readiness.Ready || DateTime.UtcNow >= deadline)
            {
                return probe;
            }

            await Task.Delay(timing.Poll, ct).ConfigureAwait(false);
        }
    }

    private async Task<(Pane? Pane, Readiness Readiness)> ProbeAsync(Project project, CancellationToken ct)
    {
        var panes = await deps.Mux.ListPanesAsync(ct).ConfigureAwait(false);
        var pane = MainPane.Find(panes, project.Root, deps.DashPane(project.Name));

        if (pane is null)
        {
            return (null, Readiness.Starting);
        }

        var first = await deps.Mux.GetTextAsync(pane.Id, ct).ConfigureAwait(false);
        await Task.Delay(timing.Settle, ct).ConfigureAwait(false);
        var second = await deps.Mux.GetTextAsync(pane.Id, ct).ConfigureAwait(false);

        return (pane, PaneReadiness.Settled(first, second));
    }

    private async Task TypeAsync(string project, PaneId pane, string text, CancellationToken ct)
    {
        await _typing.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            var keys = RelayText.Keys(text, deps.Settings.Load(project).MainOrchestratorInNvim);

            await deps.Mux.SendTextAsync(pane, keys, ct).ConfigureAwait(false);
            await Task.Delay(timing.KeyDelay, ct).ConfigureAwait(false);
            await deps.Mux.SendTextAsync(pane, RelayText.Submit, ct).ConfigureAwait(false);
        }
        finally
        {
            _typing.Release();
        }
    }

    private static string Why(Pane? pane, Readiness readiness) =>
        pane is null
            ? "not showing a pane yet"
            : readiness switch
            {
                Readiness.Busy => "busy",
                Readiness.Waiting => "waiting on a question or permission prompt",
                _ => "still starting",
            };

    private void Log(string project, string message) => deps.Log.Write(LogTag.For(project, message));
}
