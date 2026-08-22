using Fleet.Ui.Models;

namespace Fleet.Features.Dashboard.ShowDashboard.Models;

public sealed record AgentBoard(
    IReadOnlyList<FleetRow> Rows,
    int Count,
    IReadOnlyList<bool> Hidden,
    IReadOnlyList<string>? Statuses = null)
{
    public bool IsHidden(int index) => index >= 0 && index < Hidden.Count && Hidden[index];

    public string StatusAt(int index) =>
        Statuses is not null && index >= 0 && index < Statuses.Count
            ? Statuses[index]
            : string.Empty;
}
