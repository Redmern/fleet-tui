using Fleet.Ui.Models;

namespace Fleet.Features.Dashboard.ShowDashboard.Models;

public sealed record SubBoard(
    IReadOnlyList<FleetRow> Rows,
    int Count,
    IReadOnlyList<bool> Hidden)
{
    public static readonly SubBoard Empty = new([], 0, []);

    public bool IsHidden(int index) => index >= 0 && index < Hidden.Count && Hidden[index];
}
