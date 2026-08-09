namespace Fleet.Features.Agents.NewAgent.Models;

public sealed record NewAgentCommand(
    string ProjectName,
    string RepositoryName,
    string RepositoryDirectory,
    string Branch,
    string BaseBranch,
    string Harness);
