namespace Fleet.Features.Todos.ShowBoard.Models;

public sealed record BoardScopes(BoardScope Project, BoardScope All, bool ShowsAll = false)
{
    public BoardScope Current => ShowsAll ? All : Project;

    public string SwitchLabel => ShowsAll ? "this project" : "all projects";

    public BoardScopes Switched() => this with { ShowsAll = !ShowsAll };
}
