using Fleet.Ui.Models;

namespace Fleet.Features.Dashboard.ShowDashboard.Models;

public sealed record NoticeBoard(
    IReadOnlyList<FleetRow> Rows,
    IReadOnlyList<string> Keys,
    IReadOnlyList<string> Worktrees,
    int Open)
{
    public static NoticeBoard Empty { get; } = new([], [], [], 0);

    public string? KeyAt(int index) => index >= 0 && index < Keys.Count ? Keys[index] : null;

    public string? WorktreeAt(int index) => index >= 0 && index < Worktrees.Count ? Worktrees[index] : null;
}