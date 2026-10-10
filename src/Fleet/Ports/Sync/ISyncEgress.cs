using Fleet.Ports.Sync.Models;
using Fleet.Shared.Results;

namespace Fleet.Ports.Sync;

public interface ISyncEgress
{
    Task<Result> SendRepoAsync(SyncRequest request, CancellationToken ct = default);

    Task<Result> SendPathsAsync(SyncRequest request, CancellationToken ct = default);

    Task<Result> SendSecretsAsync(SyncRequest request, CancellationToken ct = default);
}
