namespace Fleet.Shared.Aidlc;

public static class OwnsGlob
{
    private const string AnyDepth = "**";

    public static bool Overlap(string first, string second) =>
        Overlap(Segments(first), 0, Segments(second), 0);

    private static string[] Segments(string glob) =>
        glob.Trim().Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static bool Overlap(string[] a, int i, string[] b, int j)
    {
        if (i == a.Length || j == b.Length)
        {
            return Covers(a, i, b, j) || Covers(b, j, a, i);
        }

        if (a[i] == AnyDepth || b[j] == AnyDepth)
        {
            return true;
        }

        return SegmentsMeet(a[i], b[j]) && Overlap(a, i + 1, b, j + 1);
    }

    private static bool Covers(string[] done, int i, string[] rest, int j)
    {
        if (i < done.Length)
        {
            return false;
        }

        if (j == rest.Length)
        {
            return true;
        }

        return done.Length == 0 || !HasWildcard(done[^1]) || done[^1] == AnyDepth;
    }

    private static bool SegmentsMeet(string a, string b)
    {
        var wildA = HasWildcard(a);
        var wildB = HasWildcard(b);

        if (wildA && wildB)
        {
            return true;
        }

        if (wildA)
        {
            return Matches(a, b);
        }

        return wildB ? Matches(b, a) : string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasWildcard(string segment) => segment.IndexOfAny(['*', '?']) >= 0;

    private static bool Matches(string pattern, string text) => Matches(pattern, 0, text, 0);

    private static bool Matches(string pattern, int p, string text, int t)
    {
        while (p < pattern.Length)
        {
            var c = pattern[p];

            if (c == '*')
            {
                for (var k = t; k <= text.Length; k++)
                {
                    if (Matches(pattern, p + 1, text, k))
                    {
                        return true;
                    }
                }

                return false;
            }

            if (t == text.Length
                || (c != '?' && char.ToLowerInvariant(c) != char.ToLowerInvariant(text[t])))
            {
                return false;
            }

            p++;
            t++;
        }

        return t == text.Length;
    }
}
