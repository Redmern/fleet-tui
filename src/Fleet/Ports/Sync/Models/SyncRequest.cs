namespace Fleet.Ports.Sync.Models;

public sealed record SyncRequest(
    string Host,
    string Project,
    string Source,
    IReadOnlyList<string> Selectors,
    string CorrelationId,
    string Caller = "");
