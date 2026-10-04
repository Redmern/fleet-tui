using Fleet.Features.Head.ServeHead.Models;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mcp.Models;
using Fleet.Ports.Projects.Models;
using Fleet.Shared.Constants;
using Fleet.Shared.Settings.Enums;

namespace Fleet.Features.Head.ServeHead;

public sealed class HeadVisibility(HeadDeps deps, HeadGate gate)
{
    public async Task<McpResult> SetAsync(Project project, McpRequest request, bool visible, CancellationToken ct)
    {
        var (target, label, unknown) = Resolve(project, request);

        if (unknown is not null)
        {
            return McpResult.Error(unknown);
        }

        var lead = string.Empty;

        if (visible && !await deps.IsOpen(project, ct).ConfigureAwait(false))
        {
            if (await deps.EnsureOpen(project).ConfigureAwait(false) is { } failed)
            {
                return McpResult.Error($"could not open {project.Name}: {failed}");
            }

            lead = $"opened {project.Name}; ";
        }

        var agent = target!;
        var where = $"{label} in {project.Name}";
        var panes = await deps.Mux.ListPanesAsync(ct).ConfigureAwait(false);
        var running = panes.Any(p => AgentPaneMatch.Owns(p, agent));

        if (visible && running && !agent.Hidden)
        {
            return McpResult.Ok($"{lead}{where} is already visible.");
        }

        if (!visible && agent.Hidden)
        {
            return McpResult.Ok($"{where} is already hidden.");
        }

        if (!visible && !running)
        {
            return McpResult.Ok($"{where} is not running, so it has no pane to hide.");
        }

        var tool = running ? HarnessTool.SetAgentVisible : HarnessTool.OpenAgent;

        if (await gate.CheckAsync(project.Name, tool, where, ct).ConfigureAwait(false) is { } denied)
        {
            return McpResult.Error(denied);
        }

        if (deps.SetVisible is not { } setVisible)
        {
            return McpResult.Error("this fleet cannot show or hide panes from the head.");
        }

        var changed = await setVisible(project, agent, visible, ct).ConfigureAwait(false);

        if (!changed.Succeeded)
        {
            return McpResult.Error($"could not {(visible ? "show" : "hide")} {where}: {changed.Error}");
        }

        var state = changed.Value!.Hidden ? "hidden" : "visible";

        return McpResult.Ok(running
            ? $"{lead}{where} is now {state}."
            : $"{lead}started {where}; it is now {state}.");
    }

    private (AgentRecord? Target, string Label, string? Unknown) Resolve(Project project, McpRequest request)
    {
        var repository = request.Value(HeadTools.Repository).Trim();
        var branch = request.Value(HeadTools.Branch).Trim();
        var slug = request.Value(HeadTools.Sub).Trim();

        var agents = deps.Agents.List(project.Name);
        var subs = agents.Where(a => AgentHarness.IsOrchestrator(a.Harness)).ToList();
        var sub = slug.Length == 0 ? null : subs.FirstOrDefault(s => Same(s.Branch, slug));

        if (slug.Length > 0 && sub is null)
        {
            return (null, string.Empty,
                $"no sub-orchestrator named '{slug}' in {project.Name}. Sub-orchestrators there: "
                + List(subs.Select(s => s.Branch)) + ".");
        }

        if (repository.Length == 0 && branch.Length == 0)
        {
            return sub is null
                ? (null, string.Empty,
                    $"'{HeadTools.Repository}' and '{HeadTools.Branch}' (an agent) or '{HeadTools.Sub}' "
                    + $"(a sub-orchestrator) is required for {request.Tool}.")
                : (sub, $"sub-orchestrator {sub.Branch}", null);
        }

        if (repository.Length == 0 || branch.Length == 0)
        {
            return (null, string.Empty,
                $"'{HeadTools.Repository}' and '{HeadTools.Branch}' together name an agent for {request.Tool}; "
                + $"give both, or only '{HeadTools.Sub}' for a sub-orchestrator.");
        }

        var pool = agents
            .Where(a => !AgentHarness.IsOrchestrator(a.Harness))
            .Where(a => sub is null || Same(a.Owner, sub.Branch))
            .ToList();
        var under = sub is null ? string.Empty : $" under sub-orchestrator {sub.Branch}";
        var inRepository = pool.Where(a => Same(a.Repository, repository)).ToList();

        if (inRepository.Count == 0)
        {
            return (null, string.Empty,
                $"no agent in repository '{repository}'{under} in {project.Name}. Repositories with agents there: "
                + List(pool.Select(a => a.Repository).Distinct(StringComparer.OrdinalIgnoreCase)) + ".");
        }

        var agent = inRepository.FirstOrDefault(a => Same(a.Branch, branch));

        return agent is null
            ? (null, string.Empty,
                $"no agent on branch '{branch}' of {inRepository[0].Repository}{under} in {project.Name}. "
                + "Branches there: " + List(inRepository.Select(a => a.Branch)) + ".")
            : (agent, $"{agent.Repository}/{agent.Branch}" + (sub is null ? string.Empty : $" (under {sub.Branch})"), null);
    }

    private static string List(IEnumerable<string> names)
    {
        var list = names.Order(StringComparer.OrdinalIgnoreCase).ToList();

        return list.Count == 0 ? "none" : string.Join(", ", list);
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
