using Fleet.Shared.Constants;

namespace Fleet.Shared.Evidence;

public sealed record ReportDrift(int Commits, bool Rewritten)
{
    public static readonly ReportDrift HistoryRewritten = new(0, true);

    public string Label => Rewritten
        ? "history rewritten since report"
        : Commits == 1 ? "1 commit after report" : $"{Commits} commits after report";

    public static string HeadToCheck(string status, ReportEvidence? evidence) =>
        status.Trim().Equals(OrchestrationStatus.Done, StringComparison.OrdinalIgnoreCase)
        && evidence is { Head.Length: > 0 } recorded
            ? recorded.Head
            : string.Empty;
}
