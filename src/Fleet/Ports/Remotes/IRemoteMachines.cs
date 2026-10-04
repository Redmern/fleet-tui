using Fleet.Ports.Remotes.Models;

namespace Fleet.Ports.Remotes;

public interface IRemoteMachines
{
    Task<IReadOnlyList<RemoteMachine>> ListAsync(CancellationToken ct = default);

    Task ConnectAsync(string host, CancellationToken ct = default);

    Task AnswerAsync(string host, string answer, CancellationToken ct = default);

    Task DisconnectAsync(string host, CancellationToken ct = default);

    Task OpenInNewWindowAsync(string host, string project, CancellationToken ct = default);

    Task ShowHereAsync(string host, string project, CancellationToken ct = default);

    Task NewProjectAsync(string host, CancellationToken ct = default);
}