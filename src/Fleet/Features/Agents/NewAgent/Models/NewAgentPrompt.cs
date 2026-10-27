using Fleet.Shared.Results;

namespace Fleet.Features.Agents.NewAgent.Models;

public sealed record NewAgentPrompt(
    string ProjectName,
    IReadOnlyList<(string Name, string Directory, string DefaultBranch)> Repositories,
    int Selected,
    Func<string, IReadOnlyList<BranchChoice>> Branches,
    string Harness,
    Func<string, CancellationToken, Task<Result<IReadOnlyList<BranchChoice>>>>? Fetch = null);
