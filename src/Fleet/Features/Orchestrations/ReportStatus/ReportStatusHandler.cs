using System.Globalization;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Shared.Constants;
using Fleet.Shared.Mcp;
using Fleet.Shared.Results;

namespace Fleet.Features.Orchestrations.ReportStatus;

public sealed class ReportStatusHandler(IAgentStore store, Func<DateTimeOffset>? clock = null)
{
    private readonly Func<DateTimeOffset> _now = clock ?? (() => DateTimeOffset.UtcNow);

    public Result<string> Handle(string project, string caller, string status, string summary)
    {
        if (caller.Trim().Length == 0)
        {
            return Result<string>.Fail(
                "report is for sub-orchestrators and agents; you are the main orchestrator.");
        }

        var isAgent = caller.StartsWith(McpCaller.AgentPrefix, StringComparison.Ordinal);
        var id = isAgent ? caller[McpCaller.AgentPrefix.Length..] : caller;

        var record = store.List(project).FirstOrDefault(a => isAgent
            ? !AgentHarness.IsOrchestrator(a.Harness)
              && string.Equals($"{a.Repository}/{a.Branch}", id, StringComparison.OrdinalIgnoreCase)
            : AgentHarness.IsOrchestrator(a.Harness)
              && string.Equals(a.Branch, id, StringComparison.OrdinalIgnoreCase));

        if (record is null)
        {
            return Result<string>.Fail(
                isAgent ? $"no agent '{id}' in {project}." : $"no sub-orchestrator named '{id}' in {project}.");
        }

        var normalized = OrchestrationStatus.Normalize(status);

        store.Save(project, record with
        {
            Status = normalized,
            Summary = summary.Trim(),
            ReportedAt = _now().ToString("O", CultureInfo.InvariantCulture),
        });

        return Result<string>.Ok(ReportNote.For(id, normalized, summary));
    }
}
