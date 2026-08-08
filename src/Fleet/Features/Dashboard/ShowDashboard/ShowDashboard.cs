namespace Fleet.Features.Dashboard.ShowDashboard;

public sealed record DashboardRow(string Text);

/// <summary>
/// The dashboard's only real logic: turning a repository list into display rows.
/// Kept out of the view so it can be tested without a terminal.
/// </summary>
public static class DashboardRows
{
    public const string EmptyHint = "(no repositories - press 'a' to add one)";

    public static IReadOnlyList<DashboardRow> ForRepositories(
        IReadOnlyList<(string Name, string DefaultBranch)> repositories)
        => repositories.Count == 0
            ? [new DashboardRow(EmptyHint)]
            : repositories
                .Select(r => new DashboardRow($"{r.Name}   [{r.DefaultBranch}]"))
                .ToList();
}
