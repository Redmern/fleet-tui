namespace Fleet.Features.Agents.RemoveAgent.Models;

public static class AgentDisposal
{
    public const int Stop = 0;

    public const int Forget = 1;

    public const int Delete = 2;

    public static IReadOnlyList<string> Choices { get; } =
    [
        "Stop the agent, keep everything",
        "Remove the agent, keep its files",
        "Remove the agent and delete its worktree",
    ];
}
