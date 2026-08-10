namespace Fleet.Features.Agents.RemoveAgent.Models;

public static class AgentDisposal
{
    public const int Opens = 0;

    public const int Hide = 1;

    public const int Stop = 2;

    public const int Forget = 3;

    public const int Delete = 4;

    public static IReadOnlyList<string> Choices { get; } =
    [
        "Change what it opens",
        "Hide or show it in the terminal",
        "Stop the agent, keep everything",
        "Remove the agent, keep its files",
        "Remove the agent and delete its worktree",
    ];
}
