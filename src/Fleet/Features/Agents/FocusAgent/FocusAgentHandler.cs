using Fleet.Ports.Mux;
using Fleet.Shared;
using Fleet.Shared.Results;

namespace Fleet.Features.Agents.FocusAgent;

public sealed class FocusAgentHandler(IMuxDriver mux)
{
    public async Task<Result> HandleAsync(string worktree, CancellationToken ct = default)
    {
        var panes = await mux.ListPanesAsync(ct).ConfigureAwait(false);

        var match = panes.FirstOrDefault(p => PathKey.Same(p.Cwd, worktree));

        if (match is null)
        {
            return Result.Fail("That agent has no pane any more. Start it again.");
        }

        await mux.FocusPaneAsync(match.Id, ct).ConfigureAwait(false);

        return Result.Ok();
    }
}
