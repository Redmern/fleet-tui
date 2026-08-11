namespace Fleet.Ui.Models;

public sealed record FleetRow(
    IReadOnlyList<FleetSpan> Spans, IReadOnlyList<FleetSpan>? Trailing = null)
{
    public string Text => string.Concat(Spans.Concat(Trailing ?? []).Select(s => s.Text));

    public static FleetRow Plain(string text) => new([FleetSpan.Plain(text)]);

    public override string ToString() => Text;
}
