namespace Fleet.Features.Todos.ShowBoard.Models;

public sealed record BoardScope(string Title, IReadOnlyList<BoardProject> Projects, bool ShowsProject)
{
    public const string AllTitle = "all projects — todo board";

    public static BoardScope One(string project, string projectRoot) =>
        new(BoardRows.Title(project), [new BoardProject(project, projectRoot)], ShowsProject: false);

    public static BoardScope All(IEnumerable<BoardProject> projects) =>
        new(AllTitle, [.. projects.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)], ShowsProject: true);
}
