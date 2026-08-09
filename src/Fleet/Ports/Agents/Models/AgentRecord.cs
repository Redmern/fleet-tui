namespace Fleet.Ports.Agents.Models;

public sealed record AgentRecord(
    string Worktree,
    string Repository,
    string Branch,
    string Harness,
    string BaseRef,
    bool RepositoryWasBare,
    bool Hidden = false);
