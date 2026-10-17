using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;

namespace Fleet.Shared.Settings;

public static class SettingsDefaults
{
    public const string Trigger = ",";

    public const string TodoTrigger = "+";

    public const string JiraJql = "assignee = currentUser() AND resolution = EMPTY";

    public const string JiraStoryPointsField = "customfield_10016";

    public const ActionPolicy Commit = ActionPolicy.Allow;

    public const ActionPolicy Push = ActionPolicy.Allow;

    public const ActionPolicy Merge = ActionPolicy.Allow;

    public static AidlcSettings Aidlc => AidlcSettings.Default;

    public const bool MainOrchestratorInNvim = true;

    public const bool SubOrchestratorsInNvim = true;

    public static RoleModel MainModel => RoleModel.Inherit;

    public static RoleModel SubModel { get; } = new("sonnet", "medium");

    public static RoleModel AgentModel => RoleModel.Inherit;

    public static RoleModel HeadModel => RoleModel.Inherit;

    public static RoleModels Models { get; } = new(MainModel, SubModel, AgentModel);

    public const bool AutoClose = false;

    public const int AutoCloseMinutes = 30;

    public const string CommitLabel = "Agents commit changes";

    public const string PushLabel = "Agents push changes";

    public const string MergeLabel = "Agents merge pull requests";

    public const string MainOrchestratorInNvimLabel = "Main orchestrator in nvim";

    public const string SubOrchestratorsInNvimLabel = "Sub-orchestrators in nvim";

    public const string AutoCloseLabel = "Auto-close idle agents";

    public const bool StatusHooks = true;

    public const string StatusHooksLabel = "Live status via hooks";

    public const bool SubagentGuidance = true;

    public const bool Iso = false;

    public const bool ShowMenuKeys = true;

    public const NvimConfig Nvim = NvimConfig.Fleet;
    public const ButtonHints ButtonHints = Enums.ButtonHints.Tooltips;

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
        HarnessTool.ListSubs,
        HarnessTool.StopSub,
        HarnessTool.RemoveSub,
        HarnessTool.Report,
        HarnessTool.ListForwards,
        HarnessTool.ForwardPort,
        HarnessTool.UnforwardPort,
        HarnessTool.OpenUrl,
        HarnessTool.StartStack,
        HarnessTool.StopStack,
        HarnessTool.TodoAdd,
        HarnessTool.CreateProject,
    ];

    public static ToolRule RuleFor(HarnessTool tool) =>
        tool == HarnessTool.RemoveRepository ? new ToolRule(ActionPolicy.Forbid, AskChannel.Both) : ToolRule.Allow;

    public static bool ServedByProjectMcp(HarnessTool tool) =>
        tool is not (HarnessTool.DeleteWorktree or HarnessTool.CreateProject);

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
        HarnessTool.ListSubs => "List the sub-orchestrators",
        HarnessTool.StopSub => "Stop a sub-orchestrator",
        HarnessTool.RemoveSub => "Remove a sub-orchestrator",
        HarnessTool.Report => "Report its own status",
        HarnessTool.ListForwards => "List forwarded web ports",
        HarnessTool.ForwardPort => "Forward a remote port to localhost",
        HarnessTool.UnforwardPort => "Stop forwarding a remote port",
        HarnessTool.OpenUrl => "Open a forwarded port in the browser",
        HarnessTool.StartStack => "Run a remote project's stack",
        HarnessTool.StopStack => "Stop a remote project's stack",
        HarnessTool.SyncToRemote => "Send work to another machine",
        HarnessTool.TodoAdd => "Add a todo ticket",
        HarnessTool.CreateProject => "Create a project",
        _ => tool.ToString(),
    };
}
