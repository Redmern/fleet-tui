namespace Fleet.Features.Agents.NewAgent.Models;

public sealed record NewAgentCommand(
    string ProjectName,
    string RepositoryName,
    string RepositoryDirectory,
    string BranchName,
    string Base,
    string Harness);
