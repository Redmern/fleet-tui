using System.Globalization;
using System.Text;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Enums;
using Fleet.Ports.Mux.Models;
using Fleet.Shared.Constants;
using Fleet.Shared.Harness;
using Fleet.Shared.Messaging;

namespace Fleet.Features.Agents.TellAgent;

public sealed class TellAgentHandler(
    IMuxDriver mux, TimeSpan? enterDelay = null, IAgentInboxes? inboxes = null, TimeProvider? clock = null)
{
    private const int AppendAttempts = 50;

    private static readonly TimeSpan AppendRetry = TimeSpan.FromMilliseconds(20);

    private static readonly SemaphoreSlim AppendGate = new(1, 1);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    private readonly TimeSpan _enterDelay = enterDelay ?? TimeSpan.FromMilliseconds(400);

    public static bool PumpedByNvim(AgentRecord agent) =>
        AgentHarness.IsOrchestrator(agent.Harness)
            ? agent.StartedInNvim
            : agent.HostedInNvim && agent.StartedInNvim;

    public static string HarnessOf(AgentRecord agent) => agent.RunningHarness;

    public async Task<string?> RouteAsync(
        AgentRecord agent,
        PaneId pane,
        string message,
        bool typed,
        string callerHarness = AgentHarness.Claude,
        CancellationToken ct = default)
    {
        if (!typed
            && HarnessRoute.Native(callerHarness, HarnessOf(agent))
            && inboxes is not null
            && await inboxes.AddressAsync(agent.Worktree, ct).ConfigureAwait(false) is { } address)
        {
            return address;
        }

        await DeliverAsync(agent, pane, message, ct).ConfigureAwait(false);

        return null;
    }

    public async Task<PromptDelivery> DeliverAsync(
        AgentRecord agent,
        PaneId pane,
        string message,
        CancellationToken ct = default,
        TimeSpan? echoWithin = null)
    {
        var dir = FleetDirectory(agent);
        await File.WriteAllTextAsync(
            Path.Combine(dir, AgentHarness.AgentInstructionFile), InstructionFor(agent, message), ct)
            .ConfigureAwait(false);

        return await PromptAsync(agent, pane, AgentHarness.AgentInstructionPrompt, echoWithin ?? TimeSpan.Zero, ct)
            .ConfigureAwait(false);
    }

    public async Task NotifyAsync(AgentRecord owner, PaneId pane, string notice, CancellationToken ct = default)
    {
        var dir = FleetDirectory(owner);
        var line = $"{_clock.GetUtcNow().ToString("O", CultureInfo.InvariantCulture)} {notice.Trim()}{Environment.NewLine}";

        await AppendAsync(Path.Combine(dir, AgentHarness.NoticesFile), Encoding.UTF8.GetBytes(line), ct)
            .ConfigureAwait(false);

        _ = await PromptAsync(owner, pane, AgentHarness.NoticesPrompt, TimeSpan.Zero, ct).ConfigureAwait(false);
    }

    private async Task AppendAsync(string path, byte[] line, CancellationToken ct)
    {
        await AppendGate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await using var file = new FileStream(
                        path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, bufferSize: 1, FileOptions.Asynchronous);
                    file.Seek(0, SeekOrigin.End);
                    await file.WriteAsync(line, ct).ConfigureAwait(false);

                    return;
                }
                catch (IOException) when (attempt < AppendAttempts)
                {
                    await Task.Delay(AppendRetry, _clock, ct).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            AppendGate.Release();
        }
    }

    private static string FleetDirectory(AgentRecord agent)
    {
        var dir = Path.Combine(agent.Worktree, ".fleet");
        Directory.CreateDirectory(dir);

        return dir;
    }

    private async Task<PromptDelivery> PromptAsync(
        AgentRecord agent, PaneId pane, string prompt, TimeSpan echoWithin, CancellationToken ct)
    {
        if (PumpedByNvim(agent))
        {
            return PromptDelivery.Pumped;
        }

        var retry = HarnessRegistry.For(agent.RunningHarness).InputRetry;

        var echo = PromptEcho.Echoed;
        var checkedEcho = retry > TimeSpan.Zero && echoWithin > TimeSpan.Zero;

        if (checkedEcho)
        {
            echo = await TypedPrompt
                .UntilEchoedAsync(mux, pane, prompt, retry, echoWithin, _enterDelay, clock: _clock, ct: ct)
                .ConfigureAwait(false);
        }
        else
        {
            await mux.SendTextAsync(pane, prompt, ct).ConfigureAwait(false);
            await Task.Delay(_enterDelay, _clock, ct).ConfigureAwait(false);
        }

        if (echo != PromptEcho.Lost)
        {
            await mux.SendTextAsync(pane, "\r", ct).ConfigureAwait(false);
        }

        return !checkedEcho ? PromptDelivery.Unchecked : TypedPrompt.DeliveryOf(echo);
    }

    public static string InstructionFor(AgentRecord agent, string message) =>
        AgentHarness.IsOrchestrator(agent.Harness) ? message : $"{message}\n\n{AgentHarness.AgentReportRule}";
}
