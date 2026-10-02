using Fleet.Shared.Aidlc.Enums;
using Fleet.Shared.Aidlc.Models;
using Fleet.Shared.Results;

namespace Fleet.Shared.Aidlc;

public sealed class UnitGraph(IReadOnlyList<WorkUnit> units)
{
    private readonly Dictionary<string, WorkUnit> _byId = units
        .Where(u => u.Id.Trim().Length > 0)
        .GroupBy(u => u.Id.Trim(), StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<WorkUnit> Units => units;

    public Result Validate(IEnumerable<string> repositories, IEnumerable<string> criteria)
    {
        var problems = Problems(repositories, criteria);

        return problems.Count == 0 ? Result.Ok() : Result.Fail(string.Join("; ", problems));
    }

    public IReadOnlyList<string> Problems(IEnumerable<string> repositories, IEnumerable<string> criteria)
    {
        var problems = new List<string>();
        var known = repositories.ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (units.Count == 0)
        {
            problems.Add("the plan has no units");
        }

        if (units.Any(u => u.Id.Trim().Length == 0))
        {
            problems.Add("every unit needs an id");
        }

        problems.AddRange(Duplicates(units.Select(u => u.Id.Trim()).Where(id => id.Length > 0))
            .Select(id => $"unit id {id} is used more than once"));

        problems.AddRange(Duplicates(units.Select(u => u.Branch.Trim()).Where(b => b.Length > 0))
            .Select(branch => $"branch {branch} is used by more than one unit"));

        foreach (var unit in units)
        {
            if (unit.Branch.Trim().Length == 0)
            {
                problems.Add($"unit {unit.Id} has no branch");
            }

            if (!known.Contains(unit.Repository.Trim()))
            {
                problems.Add($"unit {unit.Id} names an unknown repository '{unit.Repository}'");
            }

            problems.AddRange(unit.DependsOn
                .Where(d => !_byId.ContainsKey(d.Trim()))
                .Select(d => $"unit {unit.Id} depends on an unknown unit '{d}'"));
        }

        if (Cycle() is { Count: > 0 } cycle)
        {
            problems.Add($"the dependencies form a cycle: {string.Join(" -> ", cycle)}");
        }

        var trace = Traceability.Check(criteria, units);

        problems.AddRange(trace.Uncovered.Select(c => $"{c} is not covered by any unit"));
        problems.AddRange(trace.Unknown.Select(c => $"a unit claims {c}, which the spec does not define"));

        return problems;
    }

    public IReadOnlyList<string> Cycle()
    {
        var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var path = new List<string>();

        foreach (var id in _byId.Keys)
        {
            if (Visit(id, state, path) is { } found)
            {
                return found;
            }
        }

        return [];
    }

    public IReadOnlyList<WorkUnit> Ready(IReadOnlyDictionary<string, UnitState> states)
    {
        UnitState StateOf(string id) =>
            states.TryGetValue(id.Trim(), out var s) ? s : UnitState.Blocked;

        bool Settled(string id) => StateOf(id) is UnitState.Done or UnitState.Skipped;

        return units
            .Where(u => StateOf(u.Id) is UnitState.Blocked or UnitState.Ready)
            .Where(u => u.DependsOn.All(d => _byId.ContainsKey(d.Trim()) && Settled(d)))
            .Where(u => SkeletonFirst(u, Settled))
            .ToList();
    }

    public bool Concurrent(WorkUnit first, WorkUnit second) =>
        !string.Equals(first.Id, second.Id, StringComparison.OrdinalIgnoreCase)
        && !DependsOn(first, second.Id)
        && !DependsOn(second, first.Id);

    public IReadOnlyList<OwnsOverlap> Overlaps()
    {
        var overlaps = new List<OwnsOverlap>();

        for (var i = 0; i < units.Count; i++)
        {
            for (var j = i + 1; j < units.Count; j++)
            {
                var a = units[i];
                var b = units[j];

                if (!string.Equals(a.Repository.Trim(), b.Repository.Trim(), StringComparison.OrdinalIgnoreCase)
                    || !Concurrent(a, b))
                {
                    continue;
                }

                var hit = a.Owns
                    .SelectMany(x => b.Owns.Select(y => (x, y)))
                    .FirstOrDefault(p => OwnsGlob.Overlap(p.x, p.y));

                if (hit != default)
                {
                    overlaps.Add(new OwnsOverlap(a.Id, b.Id, hit.x, hit.y));
                }
            }
        }

        return overlaps;
    }

    public bool DependsOn(WorkUnit unit, string ancestor)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>(unit.DependsOn.Select(d => d.Trim()));

        while (pending.Count > 0)
        {
            var id = pending.Pop();

            if (string.Equals(id, ancestor.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (seen.Add(id) && _byId.TryGetValue(id, out var next))
            {
                foreach (var d in next.DependsOn)
                {
                    pending.Push(d.Trim());
                }
            }
        }

        return false;
    }

    private bool SkeletonFirst(WorkUnit unit, Func<string, bool> settled) =>
        unit.Skeleton || units.Where(u => u.Skeleton).All(u => settled(u.Id));

    private List<string>? Visit(string id, Dictionary<string, int> state, List<string> path)
    {
        if (state.TryGetValue(id, out var mark))
        {
            if (mark == 1)
            {
                return [.. path.SkipWhile(p => !string.Equals(p, id, StringComparison.OrdinalIgnoreCase)), id];
            }

            return null;
        }

        state[id] = 1;
        path.Add(_byId[id].Id.Trim());

        foreach (var dep in _byId[id].DependsOn.Select(d => d.Trim()).Where(_byId.ContainsKey))
        {
            if (Visit(dep, state, path) is { } found)
            {
                return found;
            }
        }

        path.RemoveAt(path.Count - 1);
        state[id] = 2;

        return null;
    }

    private static IEnumerable<string> Duplicates(IEnumerable<string> values) =>
        values
            .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);
}
