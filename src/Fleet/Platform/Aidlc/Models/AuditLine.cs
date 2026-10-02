namespace Fleet.Platform.Aidlc.Models;

public sealed class AuditLine
{
    public string Timestamp { get; set; } = string.Empty;

    public string Actor { get; set; } = string.Empty;

    public string Event { get; set; } = string.Empty;

    public string Unit { get; set; } = string.Empty;

    public string Sha { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;
}
