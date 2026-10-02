namespace Fleet.Shared.Status.Models;

public sealed record AgentSnapshot(IReadOnlyList<AgentReport> Reports)
{
    public static readonly AgentSnapshot Empty = new([]);
}
