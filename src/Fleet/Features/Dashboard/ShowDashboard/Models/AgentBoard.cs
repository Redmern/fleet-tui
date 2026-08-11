using Fleet.Ui.Models;

namespace Fleet.Features.Dashboard.ShowDashboard.Models;

public sealed record AgentBoard(
    IReadOnlyList<FleetRow> Rows, int Count, IReadOnlyList<bool> Hidden)
{
    public bool IsHidden(int index) => index >= 0 && index < Hidden.Count && Hidden[index];
}
