namespace Fleet.Features.Agents.RemoveAgent.Models;

public sealed record AgentRemoval(bool Forgotten, string? Problem)
{
    public static readonly AgentRemoval Done = new(true, null);

    public static AgentRemoval Refused(string problem) => new(false, problem);

    public static AgentRemoval FolderKept(string problem) => new(true, problem);
}
