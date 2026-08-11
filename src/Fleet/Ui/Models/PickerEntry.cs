namespace Fleet.Ui.Models;

public sealed record PickerEntry(string Label, string Detail = "", string Key = "")
{
    public static IReadOnlyList<PickerEntry> Plain(IReadOnlyList<string> labels) =>
        [.. labels.Select(l => new PickerEntry(l))];
}
