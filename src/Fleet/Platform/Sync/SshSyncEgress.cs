using Fleet.Ports;
using Fleet.Ports.Settings;
using Fleet.Ports.Sync;
using Fleet.Ports.Sync.Exceptions;
using Fleet.Ports.Sync.Models;
using Fleet.Shared.Mcp;
using Fleet.Shared.Results;
using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Enums;

namespace Fleet.Platform.Sync;

public sealed class SshSyncEgress(
    IMachineSettingsStore machine,
    ISettingsStore settings,
    IFleetLog log,
    ISyncProcessRunner runner) : ISyncEgress
{
    public Task<Result> SendRepoAsync(SyncRequest request, CancellationToken ct = default) =>
        SendAsync("a repository", request, ct);

    public Task<Result> SendPathsAsync(SyncRequest request, CancellationToken ct = default) =>
        SendAsync("files", request, ct);

    public Task<Result> SendSecretsAsync(SyncRequest request, CancellationToken ct = default) =>
        SendAsync("secrets", request, ct);

    private Task<Result> SendAsync(string what, SyncRequest request, CancellationToken ct)
    {
        Gate(request);
        ct.ThrowIfCancellationRequested();

        return Task.FromResult(Result.Fail($"sending {what} to {request.Host} is not implemented yet."));
    }

    private void Gate(SyncRequest request)
    {
        if (Refusal(request) is not { } reason)
        {
            return;
        }

        log.Write(McpAudit.Refused(request.Caller, HarnessTool.SyncToRemote, reason));
        throw new SyncRefusedException(reason);
    }

    private string? Refusal(SyncRequest request)
    {
        if (machine.LoadIso())
        {
            return SyncIso.MachineRefusal;
        }

        var config = settings.Load(request.Project).MergedOverDefaults();

        return SyncIso.Refused(request.Project, machine: false, config)
            ?? ToolRefusal.Forbidden(request.Project, config, HarnessTool.SyncToRemote);
    }

    private System.Diagnostics.Process OpenBridge(SyncRequest request)
    {
        Gate(request);

        return SyncSpawn.Start(runner, request.Host);
    }
}
