using Fleet.Ports.Agents.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;
using Fleet.Shared.Evidence;

namespace Fleet.Features.Mcp.ServeMcp;

public static class ToolText
{
    public static string Agents(IReadOnlyList<AgentRecord> agents, Func<AgentRecord, ReportDrift?>? afterReport = null)
    {
        var visible = agents
            .Where(a => !AgentHarness.IsOrchestrator(a.Harness))
            .ToList();

        if (visible.Count == 0)
        {
            return "No agents yet.";
        }

        return string.Join('\n', visible.Select(a => Line(a) + AfterReport(afterReport?.Invoke(a), ", ")));
    }

    public static string Repositories(IReadOnlyList<string> names) =>
        names.Count == 0 ? "No repositories yet." : string.Join('\n', names);

    public static string Agent(AgentRecord agent, BranchState state, bool baseMeasured = true) =>
        $"{AgentKey.Describe(agent.Repository, agent.Branch)}: {Where(agent)}, {Base(agent, baseMeasured)}{Drift(state)}{AfterReport(state.AfterReport, "; ")}{Reported(agent)}";

    public static string NotFound(string repository, string branch) =>
        $"No agent {AgentKey.Describe(repository, branch)} in this project.";

    private static string AfterReport(ReportDrift? drift, string separator) =>
        drift is null ? string.Empty : $"{separator}{drift.Label}";

    private static string Line(AgentRecord agent)
    {
        var where = agent.Hidden ? "hidden" : agent.Open ? "open" : "closed";
        var status = agent.Status.Trim().Length == 0 ? string.Empty : $" [{agent.ReportLabel}]";

        return $"{AgentKey.Describe(agent.Repository, agent.Branch)} — "
            + $"{AgentHarness.Describe(agent.Harness, agent.HostInNvim)}, {where}{status}{Evidence(agent)}";
    }

    private static string Evidence(AgentRecord agent)
    {
        var parts = new List<string>();

        if (agent.Evidence is { ShortHead.Length: > 0 } evidence)
        {
            parts.Add($"HEAD {evidence.ShortHead}");
        }

        if (Finished(agent))
        {
            parts.Add($"verify {ReportFlags.Verify(agent.Evidence)}");
        }

        return parts.Count == 0 ? string.Empty : $", {string.Join(", ", parts)}";
    }

    private static bool Finished(AgentRecord agent) =>
        OrchestrationStatus.Normalize(agent.Status) is OrchestrationStatus.Done or OrchestrationStatus.Failed
        && agent.Status.Trim().Length > 0;

    private static string Reported(AgentRecord agent)
    {
        if (agent.Status.Trim().Length == 0)
        {
            return string.Empty;
        }

        var lines = new List<string> { $"\nreport: {agent.ReportLabel}" };

        if (agent.ReportedAt.Length > 0)
        {
            lines[0] += $" at {agent.ReportedAt}";
        }

        if (agent.Summary.Length > 0)
        {
            lines.Add($"summary: {agent.Summary}");
        }

        if (agent.Evidence is { } e)
        {
            lines.Add($"worktree: {e.Worktree}");
            lines.Add($"base ref: {Shown(e.BaseRef)}");
            lines.Add($"merge-base: {Shown(e.MergeBase)}");
            lines.Add($"HEAD: {Shown(e.Head)}");
            lines.Add($"worktree dirty: {(e.Dirty ? "yes" : "no")}");
        }
        else
        {
            lines.Add("evidence: none recorded (no git state was captured with this report)");
        }

        if (Finished(agent))
        {
            lines.Add($"verify: {ReportFlags.Verify(agent.Evidence)}");

            if (agent.Evidence is { VerifyCommand.Length: > 0 } c)
            {
                lines.Add($"verify command: {c.VerifyCommand}");
            }

            if (agent.Evidence is { VerifyExitCode: { } code })
            {
                lines.Add($"verify exit code: {code}");
            }

            if (agent.Evidence is { VerifyOutput.Length: > 0 } o)
            {
                lines.Add($"verify output (last lines):\n{o.VerifyOutput}");
            }
        }

        return string.Join('\n', lines);
    }

    private static string Shown(string value) => value.Length == 0 ? "(unknown)" : value;

    private static string Base(AgentRecord agent, bool measured)
    {
        var baseRef = agent.BaseRef.Trim();

        if (baseRef.Length == 0)
        {
            return string.Empty;
        }

        return measured
            ? $"base {baseRef}: "
            : $"base {baseRef} (not found; counts are against its upstream): ";
    }

    private static string Where(AgentRecord agent) =>
        agent.Hidden ? "hidden" : agent.Open ? "open" : "closed";

    private static string Drift(BranchState state)
    {
        var parts = new List<string>();

        if (state.Ahead > 0)
        {
            parts.Add($"{state.Ahead} ahead");
        }

        if (state.Behind > 0)
        {
            parts.Add($"{state.Behind} behind");
        }

        if (state.Dirty)
        {
            parts.Add("dirty");
        }

        return parts.Count == 0 ? "in sync" : string.Join(", ", parts);
    }
}
