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
    Func<string, string, IReadOnlyList<string>> approveCommand) : IApprovalChannel
{
    public const string Title = "approve?";

    public async Task<ApprovalOutcome> AskAsync(ApprovalRequest request, CancellationToken ct = default)
    {
        var over = mux.CurrentPane;

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
