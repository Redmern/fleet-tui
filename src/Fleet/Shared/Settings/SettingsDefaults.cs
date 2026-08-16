using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;

namespace Fleet.Shared.Settings;

public static class SettingsDefaults
{
    public const string Trigger = ",";

    public static IReadOnlyDictionary<HarnessTool, ToolRule> Rules { get; } =
        HarnessToolIds.All.ToDictionary(t => t, RuleFor);

    public static IReadOnlyList<HarnessTool> Configurable { get; } =
    [
        HarnessTool.ListAgents,
        HarnessTool.ListRepositories,
        HarnessTool.ListBranches,
        HarnessTool.AgentStatus,
        HarnessTool.RepositoryStatus,
        HarnessTool.LogTail,
        HarnessTool.NewAgent,
        HarnessTool.OpenAgent,
        HarnessTool.SetAgentVisible,
        HarnessTool.StopAgent,
        HarnessTool.ChangeHarness,
        HarnessTool.TellAgent,
        HarnessTool.RemoveAgent,
        HarnessTool.DeleteWorktree,
        HarnessTool.AddRepository,
        HarnessTool.PullRepository,
        HarnessTool.SetDefaultBranch,
        HarnessTool.DistributeSecrets,
        HarnessTool.RemoveRepository,
        HarnessTool.Dispatch,
        HarnessTool.Report,
    ];

    public static ToolRule RuleFor(HarnessTool tool) =>
        IsRead(tool) ? ToolRule.Allow : ToolRule.Ask;

    public static bool IsRead(HarnessTool tool) => tool
        is HarnessTool.ListAgents
        or HarnessTool.ListRepositories
        or HarnessTool.ListBranches
        or HarnessTool.AgentStatus
        or HarnessTool.RepositoryStatus
        or HarnessTool.LogTail
        or HarnessTool.Report;

    public static string Describe(HarnessTool tool) => tool switch
    {
        HarnessTool.ListAgents => "List the agents",
        HarnessTool.ListRepositories => "List the repositories",
        HarnessTool.ListBranches => "List a repository's branches",
        HarnessTool.AgentStatus => "Inspect an agent",
        HarnessTool.RepositoryStatus => "Inspect a repository",
        HarnessTool.LogTail => "Read the fleet log",
        HarnessTool.NewAgent => "Start a new agent",
        HarnessTool.OpenAgent => "Open an agent",
        HarnessTool.SetAgentVisible => "Hide or show an agent",
        HarnessTool.StopAgent => "Stop an agent",
        HarnessTool.RemoveAgent => "Remove an agent, keep its files",
        HarnessTool.DeleteWorktree => "Delete an agent's worktree",
        HarnessTool.ChangeHarness => "Change what an agent opens",
        HarnessTool.TellAgent => "Send an instruction to an agent",
        HarnessTool.AddRepository => "Add a repository",
        HarnessTool.PullRepository => "Pull a repository",
        HarnessTool.RemoveRepository => "Delete a repository",
        HarnessTool.SetDefaultBranch => "Change a default branch",
        HarnessTool.DistributeSecrets => "Copy secrets into worktrees",
        HarnessTool.Dispatch => "Dispatch a sub-orchestrator",
        HarnessTool.Report => "Report its own status",
        _ => tool.ToString(),
    };
}
