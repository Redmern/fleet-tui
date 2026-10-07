namespace Fleet.Ui.Models;

public sealed record FleetChip(int From, int To, string Name, Action Run, IReadOnlyList<FleetSpan> Spans, string Key = "")
{
    public string Tip => FleetToolTip.Label(Name, Key);
}
