using Fleet.Shared.Results;

namespace Fleet.Ports.Orchestrations;

public interface IDispatcher
{
    Task<Result<string>> DispatchAsync(string project, string projectRoot, string prompt, CancellationToken ct = default);
}
