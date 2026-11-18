using Fleet.Shared.Constants;
using Fleet.Shared.Evidence;

namespace Fleet.Features.Orchestrations.ReportStatus;

public static class ReportNote
{
    public static string For(
        string slug, string status, string summary, ReportEvidence? evidence = null, bool orchestrator = false)
    {
        var line = summary.Trim().Length == 0
            ? $"{slug}: {ReportFlags.Label(status, orchestrator, evidence)}"
            : $"{slug}: {ReportFlags.Label(status, orchestrator, evidence)} — {summary.Trim()}";

        return line + Facts(status, orchestrator, evidence);
    }

    private static string Facts(string status, bool orchestrator, ReportEvidence? evidence)
    {
        if (evidence is null)
        {
            return string.Empty;
        }

        List<string> parts = [];

        if (evidence.ShortHead.Length > 0)
        {
            parts.Add($"HEAD {evidence.ShortHead}");
        }

        var finished = OrchestrationStatus.Normalize(status) is OrchestrationStatus.Done or OrchestrationStatus.Failed;

        if (finished && (!orchestrator || evidence.Verification != Verification.Unverified))
        {
            parts.Add($"verify {ReportFlags.Verify(evidence)}");
        }

        return parts.Count == 0 ? string.Empty : $" [{string.Join(", ", parts)}]";
    }
}
