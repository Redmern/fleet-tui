namespace Fleet.Features.Dashboard.ShowDashboard;

public static class DashboardTabs
{
    public static string Repositories(int count) => $"Repositories ({count})";

    public static string Agents(int count) => $"Agents ({count})";

    public static int Step(int current, int delta, int count) =>
        count <= 0 ? 0 : ((current + delta) % count + count) % count;
}
