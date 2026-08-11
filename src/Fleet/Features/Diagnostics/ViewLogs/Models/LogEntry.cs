namespace Fleet.Features.Diagnostics.ViewLogs.Models;

public sealed record LogEntry(
    string Stamp, string Project, string Message, IReadOnlyList<string> Details);
