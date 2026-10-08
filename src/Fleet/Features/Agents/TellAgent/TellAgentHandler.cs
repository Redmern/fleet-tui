using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Models;
using Fleet.Shared.Constants;

namespace Fleet.Features.Agents.TellAgent;

public sealed class TellAgentHandler(IMuxDriver mux, TimeSpan? enterDelay = null, IAgentInboxes? inboxes = null)
{
    private readonly TimeSpan _enterDelay = enterDelay ?? TimeSpan.FromMilliseconds(400);

    public static bool PumpedByNvim(AgentRecord agent) =>
        AgentHarness.IsOrchestrator(agent.Harness)
            ? agent.StartedInNvim
            : AgentHarness.HostedInNvim(agent.Harness);

    public async Task<string?> RouteAsync(
        AgentRecord agent, PaneId pane, string message, bool typed, CancellationToken ct = default)
    {
        if (!typed
            && inboxes is not null
            && await inboxes.AddressAsync(agent.Worktree, ct).ConfigureAwait(false) is { } address)
        {
            return address;
        }

        await DeliverAsync(agent, pane, message, ct).ConfigureAwait(false);

        return null;
    }

    public async Task DeliverAsync(AgentRecord agent, PaneId pane, string message, CancellationToken ct = default)
    {
        var dir = Path.Combine(agent.Worktree, ".fleet");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(
            Path.Combine(dir, AgentHarness.AgentInstructionFile), InstructionFor(agent, message), ct).ConfigureAwait(false);

        if (PumpedByNvim(agent))
        {
            return;
        }

        await mux.SendTextAsync(pane, AgentHarness.AgentInstructionPrompt, ct).ConfigureAwait(false);
        await Task.Delay(_enterDelay, ct).ConfigureAwait(false);
        await mux.SendTextAsync(pane, "\r", ct).ConfigureAwait(false);
    }

    private static string InstructionFor(AgentRecord agent, string message) =>
        AgentHarness.IsOrchestrator(agent.Harness) ? message : $"{message}\n\n{AgentHarness.AgentReportRule}";
}
