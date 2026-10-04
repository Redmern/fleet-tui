using Fleet.Ports.Mcp.Models;
using Fleet.Ports.Remotes;
using Fleet.Ports.Remotes.Models;

namespace Fleet.Platform.Remotes;

public sealed class NicknamedRemotes(IRemoteMachines inner, IKnownRemoteStore known) : IRemoteMachines
{
    public async Task<IReadOnlyList<RemoteMachine>> ListAsync(CancellationToken ct = default)
    {
        var machines = await inner.ListAsync(ct).ConfigureAwait(false);
        if (machines.Count == 0)
        {
            return machines;
        }

        var nicknames = known.Load()
            .Where(k => k.Nickname is not null)
            .ToDictionary(k => k.Host, k => k.Nickname, StringComparer.OrdinalIgnoreCase);

        return [.. machines.Select(m => m with { Nickname = nicknames.GetValueOrDefault(m.Host) })];
    }

    public Task ConnectAsync(string host, CancellationToken ct = default) => inner.ConnectAsync(host, ct);

    public Task AnswerAsync(string host, string answer, CancellationToken ct = default) => inner.AnswerAsync(host, answer, ct);

    public Task DisconnectAsync(string host, CancellationToken ct = default) => inner.DisconnectAsync(host, ct);

    public Task OpenInNewWindowAsync(string host, string project, CancellationToken ct = default) =>
        inner.OpenInNewWindowAsync(host, project, ct);

    public Task ShowHereAsync(string host, string project, CancellationToken ct = default) =>
        inner.ShowHereAsync(host, project, ct);

    public Task NewProjectAsync(string host, CancellationToken ct = default) => inner.NewProjectAsync(host, ct);

    public Task<McpResult> HeadAsync(string host, McpRequest request, CancellationToken ct = default) =>
        inner.HeadAsync(host, request, ct);
}
