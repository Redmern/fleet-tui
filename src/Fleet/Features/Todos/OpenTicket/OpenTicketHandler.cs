using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Enums;
using Fleet.Ports.Mux.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;
using Fleet.Shared.Results;
using Fleet.Shared.Todos;

namespace Fleet.Features.Todos.OpenTicket;

public sealed class OpenTicketHandler(IMuxDriver mux)
{
    private const string TitlePrefix = "todo ";

    public static string PaneTitle(string id) => TitlePrefix + id;

    public static bool IsTicketPane(Pane pane) =>
        pane.PaneTitle.StartsWith(TitlePrefix, StringComparison.OrdinalIgnoreCase);

    public async Task<Result> HandleAsync(
        string project,
        string projectRoot,
        string id,
        CancellationToken ct = default)
    {
        var file = TodoPaths.File(projectRoot, id);

        if (!File.Exists(file))
        {
            return Result.Fail($"ticket {id} is gone: {file} does not exist.");
        }

        var panes = await mux.ListPanesAsync(ct).ConfigureAwait(false);
        var title = PaneTitle(id);

        var open = panes.FirstOrDefault(p => string.Equals(p.PaneTitle, title, StringComparison.OrdinalIgnoreCase));

        if (open is not null)
        {
            await mux.FocusPaneAsync(open.Id, ct).ConfigureAwait(false);

            return Result.Ok();
        }

        var folder = TodoPaths.Folder(projectRoot);
        var args = AgentHarness.BrowseCommandFor(title, file);

        var beside = panes.FirstOrDefault(p =>
            IsTicketPane(p) && PathKey.Same(p.Cwd, folder) && !FleetWorkspaces.IsHidden(p.SessionName));

        var pane = beside is not null
            ? await mux.SplitAsync(
                new SplitOptions(beside.Id, SplitDirection.Right)
                {
                    Percent = 50,
                    Cwd = folder,
                    Args = args,
                },
                ct).ConfigureAwait(false)
            : await mux.SpawnAsync(
                new SpawnOptions
                {
                    Cwd = folder,
                    SessionName = project,
                    WindowId = ProjectWindows.For(mux, panes, project, projectRoot, preferCaller: false),
                    Args = args,
                },
                ct).ConfigureAwait(false);

        if (pane.IsNone)
        {
            return Result.Fail($"the {mux.Name} multiplexer did not respond. Run 'fleet doctor'.");
        }

        if (beside is null)
        {
            await mux.SetTitleAsync(pane, title, ct).ConfigureAwait(false);
        }

        await mux.FocusPaneAsync(pane, ct).ConfigureAwait(false);

        return Result.Ok();
    }
}
