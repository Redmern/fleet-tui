using Fleet.Shared.Constants;

namespace Fleet.Shared.Evidence;

public static class ReportFlags
{
    public const string Unverified = "unverified";

    public const string VerifyFailed = "verify failed";

    public const string Dirty = "dirty";

    public static IReadOnlyList<string> Of(string status, bool orchestrator, ReportEvidence? evidence)
    {
        if (!status.Trim().Equals(OrchestrationStatus.Done, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        List<string> flags = [];

        switch (evidence?.Verification ?? Verification.Unverified)
        {
            case Verification.Unverified when !orchestrator:
                flags.Add(Unverified);
                break;
            case Verification.Failed:
                flags.Add(VerifyFailed);
                break;
        }

        if (!orchestrator && evidence is { Dirty: true })
        {
            flags.Add(Dirty);
        }

        return flags;
    }

    public static string Label(string status, bool orchestrator, ReportEvidence? evidence) =>
        string.Concat(
            status.Trim(),
            string.Concat(Of(status, orchestrator, evidence).Select(f => $" ({f})")));

    public static string Verify(ReportEvidence? evidence) =>
        (evidence?.Verification ?? Verification.Unverified) switch
        {
            Verification.Ok => "ok",
            Verification.Failed => "failed",
            _ => Unverified,
        };
}
