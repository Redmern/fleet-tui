using Fleet.Shared.Orchestrations.Models;

namespace Fleet.Shared.Orchestrations;

public static class OrchestrationText
{
    public const string DefaultHowYouWork =
        """
        You do not edit repositories yourself. You use the fleet MCP tools to create
        and drive agents, one per repository and branch:
          - list_repositories / list_agents to see what exists (list_agents shows
            each agent's last reported status)
          - new_agent to start an agent on a branch; pass `task` to give it its first
            instruction in the same call
          - tell_agent to send a follow-up instruction to an agent you have started
          - open_agent / stop_agent / set_agent_visible to manage them
        Agents report their own progress with the report tool, which shows up in
        list_agents. Every tool call is subject to this project's permission settings.
        A call may be allowed, refused, or held until the user answers. A refusal is
        an answer: report it, do not retry in a loop.
        """;

    public const string DefaultAidlc =
        """
        Follow this cycle for every unit of work, and say which phase you're in
        each time you update REPORT.md:
          1. Plan — decide which repositories/branches are involved and write a
             short plan into REPORT.md before starting any agent: what you'll
             build, in what order, and how you'll know it's done.
          2. Implement — dispatch agents against that plan. Keep each agent's
             brief scoped to one coherent change.
          3. Test — before treating any agent's work as finished, confirm it ran
             its project's tests (or added them, if none existed) and that they
             pass. Do not skip this because time is short.
          4. Review — read the actual diff an agent produced, not just its
             summary, before folding it into your own report as done.
          5. Report — update REPORT.md with what shipped, what didn't, and why,
             then call fleet_report.
        If a phase reveals the plan was wrong, revise the plan instead of
        pushing on with one you no longer believe.
        """;

    public static string Instructions(
        OrchestrationBrief brief, string? howYouWork = null, string? aidlc = null)
    {
        var header =
            $"""
            # Sub-orchestrator: {brief.Slug}

            You are a fleet sub-orchestrator for the project **{brief.Project}**.
            Your working directory is this folder. It is not a git repository — it is
            fleet's scratch space for this task.

            ## Your task
            Read TASK.md. It holds the request verbatim. Do not edit it.
            """;

        var footer =
            $"""
            ## How you work
            {(string.IsNullOrWhiteSpace(howYouWork) ? DefaultHowYouWork : howYouWork.Trim())}

            ## Reporting
            Write REPORT.md in this folder as you go: what you decided, which agents you
            started, what is left. Longer artefacts go in reports/.
            When you have finished — successfully or not — call fleet_report with
            status "done" or "failed" and a one-sentence summary. Until you call it,
            fleet shows this task as working.
            """;

        var sections = new List<string> { header };

        if (!string.IsNullOrWhiteSpace(aidlc))
        {
            sections.Add($"## Process\n{aidlc.Trim()}");
        }

        sections.Add(footer);

        return string.Join("\n\n", sections);
    }

    public static string Task(OrchestrationBrief brief) =>
        $"""
        # Task

        {brief.Prompt.Trim()}

        ---
        Dispatched by fleet on {brief.StampUtc} from the {brief.Project} main orchestrator.
        """;
}
