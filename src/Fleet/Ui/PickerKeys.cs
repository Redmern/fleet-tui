namespace Fleet.Ui;

public static class PickerKeys
{
    private const string Fallbacks = "abcdefghijklmnopqrstuvwxyz0123456789";

    public static IReadOnlyList<string> For(IReadOnlyList<string> labels)
    {
        var taken = new HashSet<char>();
        var keys = new List<string>(labels.Count);

        foreach (var label in labels)
        {
            keys.Add(Pick(label, taken));
        }

        return keys;
    }

    public static IReadOnlyList<string> Label(
        IReadOnlyList<string> labels, IReadOnlyList<string> keys) =>
        [.. labels.Select((l, i) => keys[i].Length == 0 ? $"   {l}" : $"{keys[i]}  {l}")];

    private static string Pick(string label, HashSet<char> taken)
    {
        foreach (var c in label)
        {
            var lower = char.ToLowerInvariant(c);

            if (char.IsAsciiLetterOrDigit(lower) && taken.Add(lower))
            {
                return lower.ToString();
            }
        }

        foreach (var c in Fallbacks)
        {
            if (taken.Add(c))
            {
                return c.ToString();
            }
        }

        return string.Empty;
    }
}
