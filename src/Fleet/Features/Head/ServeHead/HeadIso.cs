using Fleet.Features.Head.ServeHead.Models;
using Fleet.Ports.Mcp.Models;
using Fleet.Ports.Projects.Models;
using Fleet.Shared.Iso;
using Fleet.Shared.Iso.Models;
using Fleet.Shared.Settings.Enums;

namespace Fleet.Features.Head.ServeHead;

public sealed class HeadIso(HeadDeps deps, HeadGate gate, Func<McpRequest, CancellationToken, Task<McpResult>> here)
{
    public const string Failed = "failed";

    public const string NoSuchCode = "no project has that code.";

    public IsoCodes Codes(IsoConfig iso) =>
        IsoCodes.Assign(
            iso,
            deps.Projects.List().Select(p => p.Name),
            name => deps.Agents.List(name).Select(a => a.Worktree));

    public async Task<McpResult> ServeAsync(McpRequest request, IsoConfig iso, CancellationToken ct)
    {
        var codes = Codes(iso);

        switch (request.Tool)
        {
            case HeadTools.ListAgents:
                return await ListAsync(request, codes, required: false, ct).ConfigureAwait(false);

            case HeadTools.ProjectStructure:
                return await ListAsync(request, codes, required: true, ct).ConfigureAwait(false);

            case HeadTools.Relay or HeadTools.Tell or HeadTools.MenuAction:
                {
                    if (codes.ProjectNamed(request.Value(HeadTools.Project)) is not { } name)
                    {
                        return McpResult.Error(NoSuchCode);
                    }

                    var named = new Dictionary<string, string>(request.Arguments) { [HeadTools.Project] = name };
                    var result = await here(request with { Arguments = named }, ct).ConfigureAwait(false);

                    return result.IsError ? McpResult.Error(Failed) : McpResult.Ok(IsoProjection.Ack);
                }

            case HeadTools.ShowAgent or HeadTools.HideAgent:
                return McpResult.Error(IsoProjection.Refused);

            default:
                return McpResult.Error($"{request.Tool} is not served to another machine.");
        }
    }

    private async Task<McpResult> ListAsync(McpRequest request, IsoCodes codes, bool required, CancellationToken ct)
    {
        var code = request.Value(HeadTools.Project).Trim();
        List<Project> projects = [];

        if (code.Length > 0 || required)
        {
            if (codes.ProjectNamed(code) is not { } name
                || deps.Projects.List().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                    is not { } one)
            {
                return McpResult.Error(NoSuchCode);
            }

            projects.Add(one);
        }
        else
        {
            foreach (var project in deps.Projects.List())
            {
                if (await deps.IsOpen(project, ct).ConfigureAwait(false))
                {
                    projects.Add(project);
                }
            }
        }

        var lines = new List<string>();

        foreach (var project in projects.OrderBy(p => codes.Project(p.Name), StringComparer.Ordinal))
        {
            var projectCode = codes.Project(project.Name);

            if (await gate.CheckAsync(project.Name, HarnessTool.ListAgents, string.Empty, ct).ConfigureAwait(false)
                is not null)
            {
                lines.Add(IsoProjection.Line(projectCode, IsoProjection.Refused));
                continue;
            }

            var agents = deps.Agents.List(project.Name);

            if (agents.Count == 0)
            {
                lines.Add(IsoProjection.Line(projectCode, "no agents"));
                continue;
            }

            lines.AddRange(agents
                .Select(a => (Code: codes.Agent(project.Name, a.Worktree), State: IsoProjection.State(a.Status, a.Open)))
                .OrderBy(a => a.Code, StringComparer.Ordinal)
                .Select(a => IsoProjection.Line(a.Code, a.State)));
        }

        return McpResult.Ok(lines.Count == 0 ? "no project is open." : string.Join('\n', lines));
    }
}
