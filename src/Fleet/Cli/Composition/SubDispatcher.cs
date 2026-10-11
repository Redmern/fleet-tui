using Fleet.Features.Orchestrations.Dispatch;
using Fleet.Features.Orchestrations.Dispatch.Models;
using Fleet.Ports.Orchestrations;
using Fleet.Shared.Results;

namespace Fleet.Cli.Composition;

public sealed class SubDispatcher(DispatchHandler handler) : IDispatcher
{
    public async Task<Result<string>> DispatchAsync(
        string project, string projectRoot, string prompt, CancellationToken ct = default)
    {
        var reply = await handler
            .HandleAsync(new DispatchCommand(project, projectRoot, prompt), DateTimeOffset.UtcNow.ToString("O"), ct)
            .ConfigureAwait(false);

        return reply.Succeeded ? Result<string>.Ok(reply.Value!.Slug) : Result<string>.Fail(reply.Error!);
    }
}
