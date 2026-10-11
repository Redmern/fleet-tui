using Fleet.Features.Todos.ShowBoard.Models;
using Fleet.Shared.Results;

namespace Fleet.Features.Todos.ShowBoard;

public static class BoardPull
{
    public static async Task<IReadOnlyList<(string Project, Result<int> Pulled)>> AllAsync(
        IReadOnlyList<BoardJira> pulls, CancellationToken ct)
    {
        var results = new List<(string, Result<int>)>();

        foreach (var pull in pulls)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            results.Add((pull.Project.Name, await BoardRows.SafelyAsync(() => pull.Pull(ct)).ConfigureAwait(false)));
        }

        return results;
    }
}
