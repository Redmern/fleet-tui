using Fleet.Features.Mcp.ServeMcp.Models;
using Fleet.Shared.Mcp;
using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Enums;

namespace Fleet.Features.Mcp.ServeMcp;

public static class McpTools
{
    public const string ServerName = McpServerId.ServerName;

    private static readonly ToolParam Repository =
        new(ToolArguments.Repository, "string", "The repository name.", true);

    private static readonly ToolParam Branch =
        new(ToolArguments.Branch, "string", "The agent's branch.", true);

    private static readonly ToolParam Slug =
        new(ToolArguments.Slug, "string", "The sub-orchestrator's slug, as list_subs shows it.", true);

    private static readonly ToolParam Remote =
        new(ToolArguments.Remote, "string", "The remote machine's ssh host or nickname.", true);

    private static readonly ToolParam Port =
        new(ToolArguments.Port, "integer", "The port the web app listens on, on the remote machine.", true);

    private static readonly ToolParam Project =
        new(ToolArguments.Project, "string", "The project on the remote machine.", true);

    public static string RuleId(HarnessTool tool) => McpServerId.RuleId(tool);

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
            "Start a new agent on a branch of a repository, optionally with a first task.",
            Repository,
            new ToolParam(ToolArguments.Branch, "string", "The branch to create or reuse.", true),
            new ToolParam(
                ToolArguments.Task, "string", "An optional first instruction to send the agent.", false)),
        Spec(
            HarnessTool.TellAgent,
            "Send an instruction to a running agent's Claude. When that Claude takes cross-session messages, "
            + "the result names its address and you send the instruction with SendMessage; otherwise fleet types it in.",
            Repository,
            Branch,
            new ToolParam(ToolArguments.Message, "string", "The instruction to send.", true),
            new ToolParam(
                ToolArguments.Typed,
                "boolean",
                "true to have fleet type it into the pane; only when SendMessage could not reach the session.",
                false)),
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
            "Dispatch a task. Without a repository, a sub-orchestrator carries it out. With a repository, "
            + "fleet starts a repository agent with the task directly, unless Ai-DLC applies or it is research.",
            new ToolParam(ToolArguments.Message, "string", "The task.", true),
            new ToolParam(
                ToolArguments.Repository,
                "string",
                "Optional: the one repository the task touches. Also needs new_agent to be allowed.",
                false),
            new ToolParam(
                ToolArguments.Branch,
                "string",
                "Optional, with repository: the branch to create or reuse; otherwise named from the task.",
                false),
            new ToolParam(
                ToolArguments.Research,
                "boolean",
                "Optional: true for research; the sub-orchestrator does it itself without repository agents.",
                false),
            new ToolParam(
                ToolArguments.Profile,
                "string",
                "Optional Ai-DLC profile: express, bugfix, feature, refactor or research. A profile prefix in the message (e.g. \"feature: ...\") wins over this. Used only when Ai-DLC mode is on; in manual mode only a prefix applies Ai-DLC.",
                false)),
        Spec(
            HarnessTool.ListSubs,
            "List every sub-orchestrator in this project with its status, last report and agents."),
        Spec(
            HarnessTool.StopSub,
            "Stop a sub-orchestrator: close its pane, keeping its record and folder.",
            Slug),
        Spec(
            HarnessTool.RemoveSub,
            "Remove a sub-orchestrator that is done or failed. Its agents stay registered as "
            + "top-level agents unless remove_agents is set.",
            Slug,
            new ToolParam(
                ToolArguments.DeleteFolder,
                "boolean",
                "true to also delete its orchestration folder.",
                false),
            new ToolParam(
                ToolArguments.RemoveAgents,
                "boolean",
                "true to also remove its agents and their worktrees; an agent with uncommitted "
                + "changes or unpushed commits is kept and named instead.",
                false)),
        Spec(
            HarnessTool.Report,
            "Report your own status back to fleet (working, done, failed). Report working when you start "
            + "a new task or follow-up, done only when it is fully implemented and verified, failed if you cannot finish it.",
            new ToolParam(ToolArguments.Status, "string", "working, done, or failed.", true),
            new ToolParam(ToolArguments.Summary, "string", "A one-line summary.", false)),
        Spec(
            HarnessTool.ListForwards,
            "List web app ports forwarded over fleet's ssh links (with their localhost URL) and listening ports "
            + "that are not forwarded. On a machine viewed from another, lists the ports that machine forwards."),
        Spec(
            HarnessTool.ForwardPort,
            "Forward a remote machine's port to localhost here (127.0.0.1 only) over fleet's ssh link.",
            Remote,
            Port,
            new ToolParam(ToolArguments.LocalPort, "integer", "The local port to use instead of the same number.", false)),
        Spec(HarnessTool.UnforwardPort, "Stop forwarding a remote machine's port.", Remote, Port),
        Spec(
            HarnessTool.OpenUrl,
            "Open a forwarded port in the user's browser; on a machine viewed from another, opens it there.",
            Port,
            new ToolParam(ToolArguments.Remote, "string", "The machine the port is on, when several forward it.", false)),
        Spec(
            HarnessTool.StartStack,
            "Run a remote project's runCommand in a pane there, wait for its port, forward it and give the URL.",
            Remote,
            Project,
            new ToolParam(ToolArguments.Open, "boolean", "true to open the URL in the browser once it is up.", false)),
        Spec(HarnessTool.StopStack, "Stop the stack start_stack started.", Remote, Project),
    ];

    public static ToolSpec? Find(string name)
    {
        var tool = HarnessToolIds.Parse(name);

        return tool == HarnessTool.None ? null : All.FirstOrDefault(s => s.Tool == tool);
    }

    private static ToolSpec Spec(HarnessTool tool, string description, params ToolParam[] parameters) =>
        new(tool, HarnessToolIds.For(tool), description, parameters);
}
