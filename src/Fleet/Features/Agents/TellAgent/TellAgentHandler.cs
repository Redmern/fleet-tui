using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Models;
using Fleet.Shared.Constants;
using Fleet.Shared.Messaging;

namespace Fleet.Features.Agents.TellAgent;

public sealed class TellAgentHandler(IMuxDriver mux, TimeSpan? enterDelay = null, IAgentInboxes? inboxes = null)
{
    private readonly TimeSpan _enterDelay = enterDelay ?? TimeSpan.FromMilliseconds(400);

    public static bool PumpedByNvim(AgentRecord agent) =>
        AgentHarness.IsOrchestrator(agent.Harness)
            ? agent.StartedInNvim
            : AgentHarness.HostedInNvim(agent.Harness) && agent.StartedInNvim;

    public static string HarnessOf(AgentRecord agent) => HarnessRoute.Of(agent.Harness, agent.RunsClaude);

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

    public Task DeliverAsync(AgentRecord agent, PaneId pane, string message, CancellationToken ct = default) =>
        TypeAsync(agent, pane, InstructionFor(agent, message), ct);

    public Task NotifyAsync(AgentRecord owner, PaneId pane, string notice, CancellationToken ct = default) =>
        TypeAsync(owner, pane, notice, ct);

    private async Task TypeAsync(AgentRecord agent, PaneId pane, string instruction, CancellationToken ct)
    {
        var dir = Path.Combine(agent.Worktree, ".fleet");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(
            Path.Combine(dir, AgentHarness.AgentInstructionFile), instruction, ct).ConfigureAwait(false);

        if (PumpedByNvim(agent))
        {
            return;
        }

        await mux.SendTextAsync(pane, AgentHarness.AgentInstructionPrompt, ct).ConfigureAwait(false);
        await Task.Delay(_enterDelay, ct).ConfigureAwait(false);
        await mux.SendTextAsync(pane, "\r", ct).ConfigureAwait(false);
    }

    public static string InstructionFor(AgentRecord agent, string message) =>
        AgentHarness.IsOrchestrator(agent.Harness) ? message : $"{message}\n\n{AgentHarness.AgentReportRule}";
}
