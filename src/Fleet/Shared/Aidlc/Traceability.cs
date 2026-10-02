using Fleet.Shared.Aidlc.Models;

namespace Fleet.Shared.Aidlc;

public static class Traceability
{
    public static TraceReport Check(IEnumerable<string> criteria, IEnumerable<WorkUnit> units)
    {
        var known = criteria
            .Select(Normalize)
            .Where(c => c.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var claimed = units
            .SelectMany(u => u.Acceptance)
            .Select(Normalize)
            .Where(c => c.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var uncovered = known.Where(c => !claimed.Contains(c)).ToList();

        var unknown = claimed
            .Where(c => !known.Contains(c, StringComparer.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new TraceReport(uncovered, unknown);
    }

    private static string Normalize(string criterion) => criterion.Trim();
}
