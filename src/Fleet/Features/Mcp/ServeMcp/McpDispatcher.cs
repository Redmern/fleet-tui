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
    Func<TimeSpan?>? approvalTimeout = null,
    TimeProvider? time = null)
{
    private static readonly HashSet<HarnessTool> ReadOnly =
    [
        HarnessTool.ListAgents,
        HarnessTool.ListRepositories,
        HarnessTool.ListBranches,
        HarnessTool.AgentStatus,
        HarnessTool.RepositoryStatus,
        HarnessTool.LogTail,
        HarnessTool.ListSubs,
        HarnessTool.WaitForAgent,
        HarnessTool.ListForwards,
    ];

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    private readonly SemaphoreSlim _oneAskAtATime = new(1, 1);

    private readonly SemaphoreSlim _oneChangeAtATime = new(1, 1);

    public async Task<McpResult> HandleAsync(McpRequest request, CancellationToken ct = default)
    {
        var arrived = _time.GetUtcNow();
        request = request with { Arrived = arrived };
        var tool = HarnessToolIds.Parse(request.Tool);

        if (tool == HarnessTool.None)
        {
            return McpResult.Error($"fleet has no tool named '{SafeText.Clean(request.Tool, 60)}'.");
        }

        Log(McpAudit.Requested(caller.Caller, tool));

        var config = settings.Load(caller.Project).MergedOverDefaults();

        var asking = new Asking(arrived);

        foreach (var gated in McpGate.Gated(tool, request))
        {
            if (await PassAsync(gated, request, config, asking, ct).ConfigureAwait(false) is { } refused)
            {
                return refused;
            }
        }

        var result = ReadOnly.Contains(tool)
            ? await perform(request, ct).ConfigureAwait(false)
            : await PerformInTurnAsync(request, asking, ct).ConfigureAwait(false);

        Log(result.IsError
            ? McpAudit.Denied(caller.Caller, tool, result.Text)
            : McpAudit.Allowed(caller.Caller, tool));

        return result;
    }

    private async Task<McpResult> PerformInTurnAsync(McpRequest request, Asking asking, CancellationToken ct)
    {
        if (!await TakeTurnAsync(asking, ct).ConfigureAwait(false))
        {
            return McpResult.Error(ApprovalOutcome.TooLate(asking.Request!, asking.Wait!.Value).Reason);
        }

        try
        {
            return asking.Late(_time.GetUtcNow())
                ? McpResult.Error(ApprovalOutcome.TooLate(asking.Request!, asking.Wait!.Value).Reason)
                : await perform(request, ct).ConfigureAwait(false);
        }
        finally
        {
            _oneChangeAtATime.Release();
        }
    }

    private async Task<bool> TakeTurnAsync(Asking asking, CancellationToken ct)
    {
        if (asking.Wait is not { } wait)
        {
            await _oneChangeAtATime.WaitAsync(ct).ConfigureAwait(false);

            return true;
        }

        var left = wait - (_time.GetUtcNow() - asking.Arrived);

        return left > TimeSpan.Zero && await _oneChangeAtATime.WaitAsync(left, ct).ConfigureAwait(false);
    }

    private async Task<McpResult?> PassAsync(
        HarnessTool tool, McpRequest request, SettingsConfig config, Asking asking, CancellationToken ct)
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

        var outcome = await AskAsync(tool, request, asking, ct).ConfigureAwait(false);

        if (outcome.Allowed)
        {
            return null;
        }

        Log(McpAudit.Denied(caller.Caller, tool, outcome.Reason));

        return McpResult.Error(outcome.Reason);
    }

    private async Task<ApprovalOutcome> AskAsync(
        HarnessTool tool, McpRequest request, Asking asking, CancellationToken ct)
    {
        await _oneAskAtATime.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            var asked = new ApprovalRequest(
                caller.Project,
                HarnessToolIds.For(tool),
                ApprovalPrompt.For(tool, request, caller.Caller),
                Caller: caller.Caller,
                Timeout: approvalTimeout?.Invoke(),
                Arrived: asking.Arrived);

            asking.Request = asked;
            asking.Wait = asked.Timeout ?? ApprovalRequest.DefaultWait;

            return asking.Late(_time.GetUtcNow())
                ? ApprovalOutcome.NeverShown(asked, asking.Wait.Value)
                : await approvals.AskAsync(asked, ct).ConfigureAwait(false);
        }
        finally
        {
            _oneAskAtATime.Release();
        }
    }

    private sealed class Asking(DateTimeOffset arrived)
    {
        public DateTimeOffset Arrived { get; } = arrived;

        public ApprovalRequest? Request { get; set; }

        public TimeSpan? Wait { get; set; }

        public bool Late(DateTimeOffset now) => Wait is { } wait && now - Arrived >= wait;
    }

    private void Log(string message) => log.Write(LogTag.For(caller.Project, message));
}
