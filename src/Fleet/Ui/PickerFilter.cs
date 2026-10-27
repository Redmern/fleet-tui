using Fleet.Ui.Models;
using Terminal.Gui.Input;

namespace Fleet.Ui;

public static class PickerFilter
{
    public static IReadOnlyList<int> Apply(IReadOnlyList<PickerEntry> entries, string text)
    {
        var shown = new List<int>(entries.Count);

        for (var i = 0; i < entries.Count; i++)
        {
            if (text.Length == 0 || entries[i].Label.Contains(text, StringComparison.OrdinalIgnoreCase))
            {
                shown.Add(i);
            }
        }

        return shown;
    }

    public static int Keep(IReadOnlyList<int> shown, int previous)
    {
        if (shown.Count == 0)
        {
            return -1;
        }

        var kept = shown.ToList().IndexOf(previous);

        return kept < 0 ? 0 : kept;
    }

    public static string? Edit(string text, Key key)
    {
        if (key == FleetKeys.Back)
        {
            return text.Length == 0 ? null : text[..^1];
        }

        if (key.IsCtrl || key.IsAlt)
        {
            return null;
        }

        var rune = key.AsRune;

        return rune.Value == 0 || System.Text.Rune.IsControl(rune) ? null : text + rune;
    }

    public static IReadOnlyList<PickerEntry> Merge(
        IReadOnlyList<PickerEntry> known, IReadOnlyList<PickerEntry> fresh)
    {
        var labels = known.Select(e => e.Label).ToHashSet(StringComparer.Ordinal);

        return [.. known, .. fresh.Where(e => labels.Add(e.Label))];
    }
}
