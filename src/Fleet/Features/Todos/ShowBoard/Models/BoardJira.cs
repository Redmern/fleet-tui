using Fleet.Shared.Results;

namespace Fleet.Features.Todos.ShowBoard.Models;

public sealed record BoardJira(BoardProject Project, Func<CancellationToken, Task<Result<int>>> Pull);
