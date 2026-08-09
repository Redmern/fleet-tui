namespace Fleet.Features.Agents.NewAgent.Models;

public sealed record NewAgentPrompt(
    string ProjectName,
    IReadOnlyList<(string Name, string Directory)> Repositories,
    int Selected,
    Func<string, IReadOnlyList<BranchChoice>> Branches,
    string Harness);
