namespace Fleet.Shared.Aidlc.Models;

public sealed record WorkUnit(
    string Id,
    string Title,
    string Repository,
    string Branch,
    IReadOnlyList<string> DependsOn,
    IReadOnlyList<string> Acceptance,
    IReadOnlyList<string> Owns,
    string Verify,
    bool Skeleton = false);
