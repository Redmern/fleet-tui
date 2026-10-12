using Fleet.Features.Head.ServeHead.Models;
using Fleet.Ports.Approvals.Models;
using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Enums;

namespace Fleet.Features.Head.ServeHead;

public sealed class HeadGate(HeadDeps deps)
{
    public const string Caller = "the head orchestrator";

    public ActionPolicy PolicyFor(string project, HarnessTool tool) =>
        deps.Settings.Load(project).MergedOverDefaults().RuleFor(tool).Policy;

    public string? Refused(string project, HarnessTool tool) =>
        ToolRefusal.Forbidden(project, deps.Settings.Load(project).MergedOverDefaults(), tool);

    public async Task<string?> CheckAsync(string project, HarnessTool tool, string detail, CancellationToken ct)
    {
        if (Refused(project, tool) is { } refused)
        {
            return refused;
        }

        if (PolicyFor(project, tool) != ActionPolicy.Ask)
        {
            return null;
        }

        var summary = detail.Length == 0
            ? $"{Caller} wants to: {SettingsDefaults.Describe(tool)}"
            : $"{Caller} wants to: {SettingsDefaults.Describe(tool)} — {Short(detail)}";

        var outcome = await deps.Approvals
            .AskAsync(new ApprovalRequest(project, HarnessToolIds.For(tool), summary), ct)
            .ConfigureAwait(false);

        return outcome.Allowed ? null : outcome.Reason;
    }

    public async Task<string?> CheckWithoutProjectAsync(HarnessTool tool, string detail, CancellationToken ct)
    {
        if (await ApproverAsync(ct).ConfigureAwait(false) is { } approver)
        {
            return await CheckAsync(approver, tool, detail, ct).ConfigureAwait(false);
        }

        var id = HarnessToolIds.For(tool);

        return MachinePolicy(tool) switch
        {
            ActionPolicy.Allow => null,
            ActionPolicy.Forbid => Forbidden(tool),
            _ => $"{id} needs approval in an open fleet dashboard, and no project is open; open a project first.",
        };
    }

    public string? ForbiddenOnThisMachine(HarnessTool tool) =>
        MachinePolicy(tool) == ActionPolicy.Forbid ? Forbidden(tool) : null;

    private ActionPolicy MachinePolicy(HarnessTool tool) =>
        deps.Settings.Load(string.Empty).MergedOverDefaults().RuleFor(tool).Policy;

    private static string Forbidden(HarnessTool tool) => $"fleet does not allow {HarnessToolIds.For(tool)}.";

    private async Task<string?> ApproverAsync(CancellationToken ct)
    {
        foreach (var project in deps.Projects.List().OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (await deps.IsOpen(project, ct).ConfigureAwait(false))
            {
                return project.Name;
            }
        }

        return null;
    }

    private static string Short(string text)
    {
        var line = text.ReplaceLineEndings(" ").Trim();

        return line.Length <= 120 ? line : line[..117] + "...";
    }
}
