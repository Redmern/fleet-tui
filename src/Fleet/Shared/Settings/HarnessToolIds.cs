using Fleet.Shared.Settings.Enums;

namespace Fleet.Shared.Settings;

public static class HarnessToolIds
{
    public static IReadOnlyList<HarnessTool> All { get; } =
        [.. Enum.GetValues<HarnessTool>().Where(t => t != HarnessTool.None)];

    public static string For(HarnessTool tool) => tool switch
    {
        HarnessTool.ListAgents => "list_agents",
        HarnessTool.ListRepositories => "list_repositories",
        HarnessTool.ListBranches => "list_branches",
        HarnessTool.AgentStatus => "agent_status",
        HarnessTool.RepositoryStatus => "repository_status",
        HarnessTool.LogTail => "log_tail",
        HarnessTool.NewAgent => "new_agent",
        HarnessTool.OpenAgent => "open_agent",
        HarnessTool.SetAgentVisible => "set_agent_visible",
        HarnessTool.StopAgent => "stop_agent",
        HarnessTool.RemoveAgent => "remove_agent",
        HarnessTool.DeleteWorktree => "delete_worktree",
        HarnessTool.ChangeHarness => "change_harness",
        HarnessTool.TellAgent => "tell_agent",
        HarnessTool.AddRepository => "add_repository",
        HarnessTool.PullRepository => "pull_repository",
        HarnessTool.RemoveRepository => "remove_repository",
        HarnessTool.SetDefaultBranch => "set_default_branch",
        HarnessTool.DistributeSecrets => "distribute_secrets",
        HarnessTool.Dispatch => "dispatch",
        HarnessTool.Report => "report",
        _ => string.Empty,
    };

    public static HarnessTool Parse(string id)
    {
        var wanted = id.Trim().ToLowerInvariant();

        if (wanted.Length == 0)
        {
            return HarnessTool.None;
        }

        foreach (var tool in All)
        {
            if (For(tool) == wanted)
            {
                return tool;
            }
        }

        return HarnessTool.None;
    }
}
