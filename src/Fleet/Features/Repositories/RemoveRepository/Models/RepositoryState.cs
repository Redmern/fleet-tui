namespace Fleet.Features.Repositories.RemoveRepository.Models;

public sealed record RepositoryState(
    bool Exists,
    IReadOnlyList<string> Worktrees,
    IReadOnlyList<string> Unpushed)
{
    public static readonly RepositoryState Gone = new(false, [], []);
}
