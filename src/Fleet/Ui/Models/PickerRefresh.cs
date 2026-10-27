namespace Fleet.Ui.Models;

public sealed record PickerRefresh(IReadOnlyList<PickerEntry> Entries, string Note = "");
