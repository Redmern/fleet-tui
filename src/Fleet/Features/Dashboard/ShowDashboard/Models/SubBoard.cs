using Fleet.Ui.Models;

namespace Fleet.Features.Dashboard.ShowDashboard.Models;

public sealed record SubBoard(
    IReadOnlyList<FleetRow> Rows,
    int Count,
    IReadOnlyList<bool> Hidden,
    IReadOnlyList<int>? Gaps = null,
    IReadOnlyList<string>? Statuses = null)
{
    public static readonly SubBoard Empty = new([], 0, []);

    public bool IsHidden(int index) => index >= 0 && index < Hidden.Count && Hidden[index];

    public string StatusAt(int index) =>
        Statuses is not null && index >= 0 && index < Statuses.Count
            ? Statuses[index]
            : string.Empty;
}
