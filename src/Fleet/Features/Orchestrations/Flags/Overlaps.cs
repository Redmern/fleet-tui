using Fleet.Features.Orchestrations.Flags.Models;

namespace Fleet.Features.Orchestrations.Flags;

public static class Overlaps
{
    private const int FilesNamed = 20;

    public static IReadOnlyList<OverlapPair> Due(
        IReadOnlyList<ChangedFiles> agents, IReadOnlyDictionary<string, IReadOnlySet<string>> sent)
    {
        var ordered = agents.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var due = new List<OverlapPair>();

        for (var i = 0; i < ordered.Count; i++)
        {
            for (var j = i + 1; j < ordered.Count; j++)
            {
                if (Shared(ordered[i], ordered[j]) is not { Count: > 0 } files)
                {
                    continue;
                }

                var pair = new OverlapPair(ordered[i].Name, ordered[j].Name, files);
                var named = Named(sent, pair.Key);

                if (files.Any(f => !named.Contains(f)))
                {
                    due.Add(pair);
                }
            }
        }

        return due;
    }

    public static string Line(OverlapPair pair)
    {
        var shown = string.Join(", ", pair.Files.Take(FilesNamed));
        var rest = pair.Files.Count > FilesNamed ? $" and {pair.Files.Count - FilesNamed} more" : string.Empty;

        return $"{pair.First} and {pair.Second} changed the same {pair.Files.Count} file(s) against their bases: "
               + $"{shown}{rest}. Nothing was blocked; check that their work does not collide";
    }

    private static List<string>? Shared(ChangedFiles first, ChangedFiles second) =>
        string.Equals(first.Repository.Trim(), second.Repository.Trim(), StringComparison.OrdinalIgnoreCase)
            ? [.. first.Files.Intersect(second.Files, StringComparer.Ordinal).Order(StringComparer.Ordinal)]
            : null;

    private static IReadOnlySet<string> Named(IReadOnlyDictionary<string, IReadOnlySet<string>> sent, string key) =>
        sent.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase)).Value
        ?? new HashSet<string>();
}
