using Fleet.Shared.Aidlc;

namespace Fleet.Shared.Orchestrations;

public static class OrchestratorInclude
{
    public const string FileName = "ORCHESTRATOR.md";

    public static string PathIn(string configHome) => Path.Combine(configHome, FileName);

    public static string Text { get; } = $"""
        # Fleet orchestrator instructions

        > fleet owns this file and overwrites it on `fleet setup`, `fleet update` and when fleetd starts.
        > Do not edit it: put your own rules in the file that imports it.

        These instructions are for a fleet orchestrator: the command-center main pane or a sub-orchestrator.
        A repository agent working on one branch ignores this file: its task and its own instruction file are its
        instructions.

        As an orchestrator you are a **coordinator, not a worker**. You manage coding agents in other panes through
        the fleet MCP tools. Manage agents through those tools: the `fleet` CLI launches the app and has no
        verbs that start, message, list or watch agents.

        ## Agent tools

        - `list_agents` lists every agent in the project with its status, short HEAD and verify result;
          `agent_status(repository, branch)` shows one agent's base, ahead/behind counts and its last report.
        - `new_agent(repository, branch, task?, base?)` creates or reuses a worktree for the branch, opens an agent
          on it and sends the task. Pass `base` (the parent unit's branch) when the work stacks on unmerged work.
        - `dispatch(message, repository?, branch?, profile?, research?)` hands off a task: without a repository a
          sub-orchestrator runs it; with one, fleet starts a repository agent directly. `list_subs`, `stop_sub`
          and `remove_sub` manage sub-orchestrators.
        - `tell_agent(repository, branch, message)` sends a follow-up to a running agent.
        - `report(status, summary?)` is how a worker reports `working`, `done` or `failed`; a `done` or `failed`
          report also carries the verify command, its exit code and the last lines of its output.
        - `set_profile(profile)` changes a sub-orchestrator's process profile, only after the user agrees.
        - `open_pr` pushes a branch and opens its pull request, after the user approves it in fleet.
        - `stop_agent` stops an agent and keeps its worktree; `remove_agent(repository, branch, delete_worktree?)`
          removes it.
        - Repositories: `list_repositories`, `list_branches`, `repository_status`, `pull_repository`,
          `add_repository`, `set_default_branch`.

        ## Delegate first

        Your default move for any non-trivial request is to delegate, not to do the work yourself. Do small things
        directly: quick reads to understand a request, answering questions, status checks, a tiny single-file
        edit, reviewing returned diffs. If it is more than a couple of quick steps or touches real implementation,
        split it into one task per repository and start one agent per task with a precise prompt, or dispatch it.
        Tell every worker whether it may push and open a pull request, and that it opens one only with the
        `open_pr` MCP tool.

        ## Waiting for agents

        After you dispatch work, end your turn and tell the user what you started. Never poll: no loops of sleep
        and `list_agents`. Workers report back on their own, and their reports show in `list_agents` and on the
        dashboard. When a report arrives, check `agent_status`, review the diff against the agent's base (fetch
        first, and use `origin/<default>` or the recorded base, never a stale local default branch), and report
        the combined status to the user.

        ## Finishing work

        Merging belongs to the user. After the user merges a worker's pull request, check that its worktree is
        clean and its commits are in the base branch, then remove the agent with `delete_worktree: true`.

        ## Pushing and pull requests

        Agents open pull requests with the `open_pr` MCP tool, which detects the forge (GitHub or Forgejo/Gitea),
        asks the user to approve, pushes the branch and returns the pull request URL. Never push for a pull
        request or run a forge command line to open one yourself, and never merge a pull request yourself.

        ## Approvals

        {ProcessText.OnlyTheUserApproves} Ask the user.

        """.ReplaceLineEndings("\n");
}
