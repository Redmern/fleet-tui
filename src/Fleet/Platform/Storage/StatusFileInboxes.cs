using Fleet.Platform.Hooks;
using Fleet.Ports.Agents;
using Fleet.Shared.Status;
using Fleet.Shared.Status.Models;

namespace Fleet.Platform.Storage;

public sealed class StatusFileInboxes(IAgentStateStore states, Func<string, bool>? socketExists = null) : IAgentInboxes
{
    private const string WindowsPipePrefix = @"\\.\pipe\";

    private readonly Func<string, bool> _socketExists = socketExists ?? Live;

    public async Task<string?> AddressAsync(string folder, CancellationToken ct = default)
    {
        var snapshot = await states.GetSnapshotAsync(ct).ConfigureAwait(false);
        var alive = snapshot.Reports.Where(r => r.Inbox.Length == 0 || _socketExists(r.Inbox));

        return AgentStatusRules.InboxOf(new AgentSnapshot([.. alive]), folder);
    }

    private static bool Live(string address)
    {
        var path = address.StartsWith(HookIo.PeerAddressPrefix, StringComparison.Ordinal) ? address[HookIo.PeerAddressPrefix.Length..] : address;

        var windowsPipe = OperatingSystem.IsWindows() && path.StartsWith(WindowsPipePrefix, StringComparison.Ordinal);

        return path.Length > 0 && (windowsPipe || File.Exists(path));
    }
}
