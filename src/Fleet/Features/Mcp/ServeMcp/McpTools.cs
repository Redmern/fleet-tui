using Fleet.Features.Mcp.ServeMcp.Models;
using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Enums;

namespace Fleet.Features.Mcp.ServeMcp;

public static class McpTools
{
    public const string ServerName = "fleet";

    private static readonly ToolParam Repository =
        new(ToolArguments.Repository, "string", "The repository name.", true);

    private static readonly ToolParam Branch =
        new(ToolArguments.Branch, "string", "The agent's branch.", true);

    public static string RuleId(HarnessTool tool) => $"mcp__{ServerName}__{HarnessToolIds.For(tool)}";

    public static IReadOnlyList<ToolSpec> All { get; } =
    [
        Spec(HarnessTool.ListAgents, "List every agent in this project with its status."),
        Spec(HarnessTool.ListRepositories, "List every repository in this project."),
        Spec(
            HarnessTool.ListBranches,
            "List the branches of a repository.",
            Repository),
        Spec(
            HarnessTool.AgentStatus,
            "Report one agent's branch state and whether it is open.",
            Repository,
            Branch),
        Spec(
            HarnessTool.RepositoryStatus,
            "Report a repository's worktrees and unpushed branches.",
            Repository),
        Spec(
            HarnessTool.LogTail,
            "Read the tail of the fleet log for this project.",
            new ToolParam(ToolArguments.Lines, "integer", "How many lines to read.", false)),
        Spec(
            HarnessTool.NewAgent,
            "Start a new agent on a branch of a repository.",
            Repository,
            new ToolParam(ToolArguments.Branch, "string", "The branch to create or reuse.", true)),
        Spec(HarnessTool.OpenAgent, "Bring an agent's pane into view.", Repository, Branch),
        Spec(
            HarnessTool.SetAgentVisible,
            "Hide an agent from the terminal or bring it back.",
            Repository,
            Branch,
            new ToolParam(ToolArguments.Visible, "boolean", "true to show, false to hide.", true)),
        Spec(HarnessTool.StopAgent, "Stop an agent, keeping its worktree.", Repository, Branch),
        Spec(
            HarnessTool.RemoveAgent,
            "Remove an agent. Set delete_worktree to also delete its files.",
            Repository,
            Branch,
            new ToolParam(
                ToolArguments.DeleteWorktree,
                "boolean",
                "true to delete the worktree too.",
                false)),
        Spec(
            HarnessTool.ChangeHarness,
            "Change what an agent opens (claude, nvim).",
            Repository,
            Branch,
            new ToolParam(ToolArguments.Harness, "string", "The harness to switch to.", true)),
        Spec(
            HarnessTool.AddRepository,
            "Add a repository to the project.",
            Repository),
        Spec(HarnessTool.PullRepository, "Pull a repository's default branch.", Repository),
        Spec(HarnessTool.RemoveRepository, "Delete a repository and its worktrees.", Repository),
        Spec(
            HarnessTool.SetDefaultBranch,
            "Change a repository's default branch.",
            Repository,
            new ToolParam(ToolArguments.DefaultBranch, "string", "The new default branch.", true)),
        Spec(HarnessTool.DistributeSecrets, "Copy secret files into a repository's worktrees.", Repository),
        Spec(
            HarnessTool.Dispatch,
            "Dispatch a sub-orchestrator to carry out a task described in a message.",
            new ToolParam(ToolArguments.Message, "string", "The task for the sub-orchestrator.", true)),
        Spec(
            HarnessTool.Report,
            "Report your own status back to fleet (working, done, failed).",
            new ToolParam(ToolArguments.Status, "string", "working, done, or failed.", true),
            new ToolParam(ToolArguments.Summary, "string", "A one-line summary.", false)),
    ];

    public static ToolSpec? Find(string name)
    {
        var tool = HarnessToolIds.Parse(name);

        return tool == HarnessTool.None ? null : All.FirstOrDefault(s => s.Tool == tool);
    }

    private static ToolSpec Spec(HarnessTool tool, string description, params ToolParam[] parameters) =>
        new(tool, HarnessToolIds.For(tool), description, parameters);
}
