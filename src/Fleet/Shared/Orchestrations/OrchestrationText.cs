using Fleet.Shared.Orchestrations.Models;

namespace Fleet.Shared.Orchestrations;

public static class OrchestrationText
{
    public static string Instructions(OrchestrationBrief brief) =>
        $"""
        # Sub-orchestrator: {brief.Slug}

        You are a fleet sub-orchestrator for the project **{brief.Project}**.
        Your working directory is this folder. It is not a git repository — it is
        fleet's scratch space for this task.

        ## Your task
        Read TASK.md. It holds the request verbatim. Do not edit it.

        ## How you work
        You do not edit repositories yourself. You use the fleet MCP tools to create
        and drive agents, one per repository and branch:
          - list_repositories / list_agents to see what exists
          - new_agent to start an agent on a branch in a repository
          - open_agent / stop_agent / set_agent_visible to manage them
        Every tool call is subject to this project's permission settings. A call may
        be allowed, refused, or held until the user answers. A refusal is an answer:
        report it, do not retry in a loop.

        ## Reporting
        Write REPORT.md in this folder as you go: what you decided, which agents you
        started, what is left. Longer artefacts go in reports/.
        When you have finished — successfully or not — call fleet_report with
        status "done" or "failed" and a one-sentence summary. Until you call it,
        fleet shows this task as working.
        """;

    public static string Task(OrchestrationBrief brief) =>
        $"""
        # Task

        {brief.Prompt.Trim()}

        ---
        Dispatched by fleet on {brief.StampUtc} from the {brief.Project} main orchestrator.
        """;
}
