using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;
using Fleet.Shared.Results;

namespace Fleet.Features.Orchestrations.ReportStatus;

public sealed class ReportStatusHandler(IAgentStore store)
{
    public Result<string> Handle(string project, string caller, string status, string summary)
    {
        if (caller.Trim().Length == 0)
        {
            return Result<string>.Fail(
                "fleet_report is for sub-orchestrators; you are the main orchestrator.");
        }

        var record = store.List(project)
            .FirstOrDefault(a => AgentHarness.IsOrchestrator(a.Harness)
                && string.Equals(a.Branch, caller, StringComparison.OrdinalIgnoreCase));

        if (record is null)
        {
            return Result<string>.Fail($"no sub-orchestrator named '{caller}' in {project}.");
        }

        var normalized = OrchestrationStatus.Normalize(status);

        store.Save(project, record with { Status = normalized });

        return Result<string>.Ok(ReportNote.For(caller, normalized, summary));
    }
}
