using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;
using Fleet.Shared.Results;

namespace Fleet.Features.Repositories.OpenRepository;

public sealed class OpenRepositoryHandler(IMuxDriver mux)
{
    public async Task<Result> HandleAsync(
        string project,
        string name,
        string container,
        string branch,
        string projectRoot,
        CancellationToken ct = default)
    {
        var directory = RepositoryWorktree.For(container, branch, Directory.Exists);

        var panes = await mux.ListPanesAsync(ct).ConfigureAwait(false);

        var window = panes.FirstOrDefault(p => PathKey.Same(p.Cwd, projectRoot))?.WindowId;

        var open = panes.FirstOrDefault(p => PathKey.Same(p.Cwd, directory));

        if (open is not null)
        {
            if (window is not null && open.WindowId != window)
            {
                await mux.MovePaneAsync(
                        open.Id, new MovePaneOptions { WindowId = window }, ct)
                    .ConfigureAwait(false);

                await mux.SetTitleAsync(open.Id, $"{name}/{BranchSlug.Of(branch)}", ct)
                    .ConfigureAwait(false);
            }

            await mux.FocusPaneAsync(open.Id, ct).ConfigureAwait(false);

            return Result.Ok();
        }

        var pane = await mux.SpawnAsync(
            new SpawnOptions
            {
                Cwd = directory,
                SessionName = project,
                WindowId = window,
                Args = AgentHarness.BrowseCommand,
            },
            ct).ConfigureAwait(false);

        if (pane.IsNone)
        {
            return Result.Fail($"the {mux.Name} multiplexer did not respond. Run 'fleet doctor'.");
        }

        await mux.SetTitleAsync(pane, $"{name}/{BranchSlug.Of(branch)}", ct).ConfigureAwait(false);

        return Result.Ok();
    }
}
