using Fleet.Ports;
using Fleet.Ports.Approvals;
using Fleet.Ports.Approvals.Models;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Enums;
using Fleet.Ports.Mux.Exceptions;
using Fleet.Ports.Mux.Models;

namespace Fleet.Features.Mcp.ServeMcp;

public sealed class FloatingApprovalChannel(
    IApprovalChannel inner,
    IMuxDriver mux,
    Func<string, string, IReadOnlyList<string>> approveCommand,
    INotifier? notifier = null) : IApprovalChannel
{
    public const string Title = "approve?";

    public async Task<ApprovalOutcome> AskAsync(ApprovalRequest request, CancellationToken ct = default)
    {
        var over = mux.CurrentPane;

        if (await NobodyCanSeeAsync(request.Project, over, ct).ConfigureAwait(false))
        {
            return ApprovalOutcome.NoDashboard($"approval needed in project {request.Project}; open it to answer");
        }

        notifier?.Notify(
            $"approval needed in {request.Project}: {request.Tool} by {request.CallerName}");

        if (over.IsNone || !mux.Caps.HasFlag(MuxCaps.Popup))
        {
            return await inner.AskAsync(request, ct).ConfigureAwait(false);
        }

        var answer = inner.AskAsync(request with { Pane = over.Value }, ct);

        if (answer.IsCompleted)
        {
            return await answer.ConfigureAwait(false);
        }

        var popup = await OpenAsync(over, request.Project, ct).ConfigureAwait(false);

        try
        {
            return await answer.ConfigureAwait(false);
        }
        finally
        {
            if (!popup.IsNone)
            {
                await CloseAsync(popup).ConfigureAwait(false);
            }
        }
    }

    private async Task<bool> NobodyCanSeeAsync(string project, PaneId over, CancellationToken ct)
    {
        if (!mux.Caps.HasFlag(MuxCaps.Workspaces))
        {
            return false;
        }

        try
        {
            var workspaces = await mux.ListWorkspacesAsync(ct).ConfigureAwait(false);
            var places = new List<string> { project };

            if (!over.IsNone
                && (await mux.ListPanesAsync(ct).ConfigureAwait(false)).FirstOrDefault(p => p.Id == over) is { } pane)
            {
                places.Add(pane.SessionName);
            }

            return places.All(place => Seen(workspaces, place) == false);
        }
        catch (MuxUnavailableException)
        {
            return false;
        }
    }

    private static bool? Seen(IReadOnlyList<Workspace> workspaces, string name)
    {
        var listed = workspaces.Where(w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();

        if (listed.Any(w => w.ShownAnywhere == true))
        {
            return true;
        }

        return listed.Any(w => w.ShownAnywhere is null) ? null : false;
    }

    private async Task<PaneId> OpenAsync(PaneId over, string project, CancellationToken ct)
    {
        try
        {
            var popup = await mux
                .SpawnFloatingAsync(over, new SpawnOptions { Args = approveCommand(project, over.Value) }, ct)
                .ConfigureAwait(false);

            if (!popup.IsNone)
            {
                await mux.SetTitleAsync(popup, Title, ct).ConfigureAwait(false);
            }

            return popup;
        }
        catch (Exception e) when (e is MuxUnavailableException or NotSupportedException)
        {
            return PaneId.None;
        }
    }

    private async Task CloseAsync(PaneId popup)
    {
        try
        {
            if ((await mux.ListPanesAsync().ConfigureAwait(false)).Any(p => p.Id == popup))
            {
                await mux.KillPaneAsync(popup).ConfigureAwait(false);
            }
        }
        catch (MuxUnavailableException)
        {
        }
    }
}
