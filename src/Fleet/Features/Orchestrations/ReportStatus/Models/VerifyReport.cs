namespace Fleet.Features.Orchestrations.ReportStatus.Models;

public sealed record VerifyReport(string Command, int? ExitCode, string Output)
{
    public bool Given => Command.Length > 0 || ExitCode is not null || Output.Length > 0;
}
