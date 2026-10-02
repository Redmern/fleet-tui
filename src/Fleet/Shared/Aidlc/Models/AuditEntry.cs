using Fleet.Shared.Aidlc.Enums;

namespace Fleet.Shared.Aidlc.Models;

public sealed record AuditEntry(
    string Timestamp,
    AuditActor Actor,
    AuditEvent Event,
    string Unit = "",
    string Sha = "",
    string Detail = "");
