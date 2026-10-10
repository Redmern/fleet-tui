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

    private static string Short(string text)
    {
        var line = text.ReplaceLineEndings(" ").Trim();

        return line.Length <= 120 ? line : line[..117] + "...";
    }
}
