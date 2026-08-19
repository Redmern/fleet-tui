namespace Fleet.Features.Dashboard.ShowDashboard;

public static class DashboardTabs
{
    public const int AgentsTab = 0;

    public const int SubsTab = 1;

    public const int RepositoriesTab = 2;

    public static string Agents(int count) => $"Agents ({count})";

    public static string Subs(int count) => $"Subs ({count})";

    public static string Repositories(int count) => $"Repositories ({count})";

    public static int Step(int current, int delta, int count) =>
        count <= 0 ? 0 : ((current + delta) % count + count) % count;
}
