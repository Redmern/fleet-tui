namespace Fleet.Features.Orchestrations.ReportStatus;

public static class ReportNote
{
    public static string For(string slug, string status, string summary) =>
        summary.Trim().Length == 0
            ? $"{slug}: {status}"
            : $"{slug}: {status} — {summary.Trim()}";
}
