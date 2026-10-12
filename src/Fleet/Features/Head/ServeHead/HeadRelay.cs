using Fleet.Features.Head.ServeHead.Enums;
using Fleet.Features.Head.ServeHead.Models;
using Fleet.Ports.Mcp.Models;
using Fleet.Ports.Mux.Exceptions;
using Fleet.Ports.Mux.Models;
using Fleet.Ports.Projects.Models;
using Fleet.Ports.Relay;
using Fleet.Ports.Relay.Models;
using Fleet.Shared;
using Fleet.Shared.Messaging;
using Fleet.Shared.Settings.Enums;

namespace Fleet.Features.Head.ServeHead;

public sealed class HeadRelay(HeadDeps deps, HeadGate gate, HeadTiming timing)
{
    private readonly Lock _lock = new();

    private readonly IRelayQueueStore _queue = deps.RelayQueue ?? new MemoryRelayQueue();

    private readonly Dictionary<string, Task> _pumps = new(StringComparer.OrdinalIgnoreCase);

    private readonly SemaphoreSlim _typing = new(1, 1);

    public int Pending(string project) => Queued(project).Count;

    public IReadOnlyList<QueuedPrompt> Queued(string project)
    {
        lock (_lock)
        {
            return _queue.Load(project);
        }
    }

    public IReadOnlyList<QueuedPrompt> Drop(string project, int? number)
    {
        lock (_lock)
        {
            var queued = _queue.Load(project).ToList();

            if (number is not { } n)
            {
                _queue.Save(project, []);
                return queued;
            }

            if (n < 1 || n > queued.Count)
            {
                return [];
            }

            var dropped = queued[n - 1];
            queued.RemoveAt(n - 1);
            _queue.Save(project, queued);

            return [dropped];
        }
    }

    public void Resume()
    {
        foreach (var name in _queue.Projects())
        {
            if (deps.Projects.List().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                is { } project)
            {
                Pump(project);
            }
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
            + $"({waiting} waiting). fleet types it in, in order, as soon as that Claude is idle, and says it "
            + $"arrives late. {Unblock(project, pane, readiness)} {HeadTools.ListQueued} shows the queue and "
            + $"{HeadTools.DropQueued} drops a stale prompt instead of queueing a duplicate. Queued: {text}");
    }

    private async Task<string?> InboxAsync(Project project, CancellationToken ct)
    {
        if (deps.Inboxes is not { } inboxes || Pending(project.Name) > 0)
        {
            return null;
        }

        var panes = await deps.Mux.ListPanesAsync(ct).ConfigureAwait(false);

        return MainPane.Find(panes, project.Root, project.Name, deps.DashPane(project.Name)) is null
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
        int count;

        lock (_lock)
        {
            var queued = _queue.Load(project.Name).ToList();
            queued.Add(new QueuedPrompt(text, DateTimeOffset.Now));
            _queue.Save(project.Name, queued);
            count = queued.Count;
        }

        Pump(project);

        return count;
    }

    private void Pump(Project project)
    {
        lock (_lock)
        {
            if (!_pumps.TryGetValue(project.Name, out var pump) || pump.IsCompleted)
            {
                _pumps[project.Name] = Task.Run(() => PumpAsync(project));
            }
        }
    }

    private async Task PumpAsync(Project project)
    {
        var fastUntil = DateTime.UtcNow + timing.QueueTimeout;

        while (true)
        {
            try
            {
                if (Queued(project.Name).FirstOrDefault() is not { } next)
                {
                    return;
                }

                if (await DeliverQueuedAsync(project, next).ConfigureAwait(false))
                {
                    fastUntil = DateTime.UtcNow + timing.QueueTimeout;
                }

                await Task.Delay(DateTime.UtcNow > fastUntil ? timing.SlowPoll : timing.Poll).ConfigureAwait(false);
            }
            catch (Exception e) when (e is MuxUnavailableException or IOException or UnauthorizedAccessException
                or InvalidOperationException)
            {
                Log(project.Name, $"head keeps its queued prompts and tries again: {e.Message}");
                await Task.Delay(timing.SlowPoll).ConfigureAwait(false);
            }
        }
    }

    private async Task<bool> DeliverQueuedAsync(Project project, QueuedPrompt next)
    {
        var (pane, readiness) = await ProbeAsync(project, CancellationToken.None).ConfigureAwait(false);

        if (pane is not { } target || readiness != Readiness.Ready)
        {
            return false;
        }

        await _typing.WaitAsync().ConfigureAwait(false);

        try
        {
            lock (_lock)
            {
                var queued = _queue.Load(project.Name).ToList();

                if (queued.Count == 0 || queued[0] != next)
                {
                    return false;
                }

                queued.RemoveAt(0);
                _queue.Save(project.Name, queued);
            }

            try
            {
                await SendAsync(project.Name, target.Id, RelayText.Late(next.Text, next.QueuedAt), CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception e) when (e is MuxUnavailableException or IOException or InvalidOperationException)
            {
                lock (_lock)
                {
                    _queue.Save(project.Name, [next, .. _queue.Load(project.Name)]);
                }

                throw;
            }
        }
        finally
        {
            _typing.Release();
        }

        Log(project.Name, $"head delivered a queued prompt to the orchestrator late: {next.Text}");
        return true;
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
        var pane = MainPane.Find(panes, project.Root, project.Name, deps.DashPane(project.Name));

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
            await SendAsync(project, pane, text, ct).ConfigureAwait(false);
        }
        finally
        {
            _typing.Release();
        }
    }

    private async Task SendAsync(string project, PaneId pane, string text, CancellationToken ct)
    {
        var keys = RelayText.Keys(text, deps.Settings.Load(project).MainOrchestratorInNvim);

        await deps.Mux.SendTextAsync(pane, keys, ct).ConfigureAwait(false);
        await Task.Delay(timing.KeyDelay, ct).ConfigureAwait(false);
        await deps.Mux.SendTextAsync(pane, RelayText.Submit, ct).ConfigureAwait(false);
    }

    private static string Unblock(Project project, Pane? pane, Readiness readiness) =>
        pane is null
            ? $"fleet finds no pane started in {project.Root} and no orchestrator in the dash tab of the "
              + $"'{project.Name}' workspace. To unblock it, run {HeadTools.SwitchProject} on {project.Name}; if the "
              + "prompt still waits, ask the user to close the project in fleet and open it again, which starts its "
              + $"orchestrator in {project.Root}."
            : readiness == Readiness.Waiting
                ? $"To unblock it, the user answers that prompt in {project.Name} ({HeadTools.SwitchProject} shows it)."
                : "Nothing to do but wait.";

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
