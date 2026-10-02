namespace Fleet.Shared.Aidlc.Models;

public sealed record TraceReport(IReadOnlyList<string> Uncovered, IReadOnlyList<string> Unknown)
{
    public bool Complete => Uncovered.Count == 0 && Unknown.Count == 0;
}
