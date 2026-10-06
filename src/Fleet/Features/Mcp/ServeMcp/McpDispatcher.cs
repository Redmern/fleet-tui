using Fleet.Ports;
using Fleet.Ports.Approvals;
using Fleet.Ports.Approvals.Models;
using Fleet.Ports.Mcp.Models;
using Fleet.Ports.Settings;
using Fleet.Shared;
using Fleet.Shared.Mcp;
using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;

namespace Fleet.Features.Mcp.ServeMcp;

public sealed class McpDispatcher(
    McpCaller caller,
    ISettingsStore settings,
    IApprovalChannel approvals,
    IFleetLog log,
    Func<McpRequest, CancellationToken, Task<McpResult>> perform,
    INotifier? notifier = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<McpResult> HandleAsync(McpRequest request, CancellationToken ct = default)
    {
        var tool = HarnessToolIds.Parse(request.Tool);

        if (tool == HarnessTool.None)
        {
            return McpResult.Error($"fleet has no tool named '{SafeText.Clean(request.Tool, 60)}'.");
        }

        Log(McpAudit.Requested(caller.Caller, tool));

        var config = settings.Load(caller.Project).MergedOverDefaults();

        foreach (var gated in McpGate.Gated(tool, request))
        {
            if (await PassAsync(gated, request, config, ct).ConfigureAwait(false) is { } refused)
            {
                return refused;
            }
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            var result = await perform(request, ct).ConfigureAwait(false);

            Log(result.IsError
                ? McpAudit.Denied(caller.Caller, tool, result.Text)
                : McpAudit.Allowed(caller.Caller, tool));

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<McpResult?> PassAsync(
        HarnessTool tool, McpRequest request, SettingsConfig config, CancellationToken ct)
    {
        var decision = McpGate.Decide(tool, config, caller.IsSub);

        if (decision.Forbidden)
        {
            Log(McpAudit.Forbidden(caller.Caller, tool));

            return McpResult.Error(
                $"This project does not allow {HarnessToolIds.For(tool)}.");
        }

        if (!decision.AsksFleet)
        {
            return null;
        }

        notifier?.Notify(
            $"approval needed: {HarnessToolIds.For(tool)} by {caller.Caller}");

        var outcome = await approvals
            .AskAsync(
                new ApprovalRequest(
                    caller.Project,
                    HarnessToolIds.For(tool),
                    ApprovalPrompt.For(tool, request, caller.Caller)),
                ct)
            .ConfigureAwait(false);

        if (outcome.Allowed)
        {
            return null;
        }

        Log(McpAudit.Denied(caller.Caller, tool, outcome.Reason));

        return McpResult.Error(outcome.Reason);
    }

    private void Log(string message) => log.Write(LogTag.For(caller.Project, message));
}
