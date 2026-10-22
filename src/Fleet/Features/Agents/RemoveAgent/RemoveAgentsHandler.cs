using Fleet.Ports.Agents.Models;
using Fleet.Shared.Constants;

namespace Fleet.Features.Agents.RemoveAgent;

public sealed class RemoveAgentsHandler(RemoveAgentHandler remover)
{
    public async Task<string> HandleAsync(
        string project,
        IReadOnlyList<(AgentRecord Agent, bool Delete)> doomed,
        CancellationToken ct = default)
    {
        var refused = new List<string>();
        var kept = new List<string>();

        foreach (var (agent, delete) in doomed)
        {
            var gone = await remover.RemoveAsync(project, agent, delete, ct).ConfigureAwait(false);

            if (gone.Problem is null)
            {
                continue;
            }

            (gone.Forgotten ? kept : refused).Add($"{Label(agent)}: {gone.Problem}");
        }

        var parts = new List<string>();

        if (refused.Count == 0)
        {
            parts.Add(doomed.Count == 1
                ? $"removed {Label(doomed[0].Agent)}."
                : $"removed {doomed.Count} agent(s).");
        }
        else
        {
            parts.Add($"could not remove: {string.Join("; ", refused)}");
        }

        parts.AddRange(kept);

        return string.Join(' ', parts);
    }

    public static string Label(AgentRecord agent) =>
        AgentHarness.IsOrchestrator(agent.Harness)
            ? agent.Branch
            : $"{agent.Repository}/{agent.Branch}";
}
