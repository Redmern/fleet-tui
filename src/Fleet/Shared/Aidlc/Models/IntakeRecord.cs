namespace Fleet.Shared.Aidlc.Models;

public sealed record IntakeRecord(IntentState State, IReadOnlyList<AuditEntry> Events);
