using Fleet.Cli.Composition;
using Fleet.Cli.Models;
using Fleet.Ports.Approvals.Enums;
using Fleet.Ports.Approvals.Models;
using Fleet.Ui;
using Terminal.Gui.App;

namespace Fleet.Cli.Commands;

public static class ApproveCommand
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    public static async Task<int> RunAsync(Invocation invocation)
    {
        if (invocation.Project is not { } project || invocation.Text is not { } pane)
        {
            await Console.Error.WriteLineAsync("usage: fleet approve --project <name> <pane>").ConfigureAwait(false);
            return 2;
        }

        var inbox = Adapters.ApprovalInbox();
        var deadline = DateTime.UtcNow + Wait;
        PendingApproval? pending;

        while ((pending = inbox.TakePending(project, pane)) is null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100).ConfigureAwait(false);
        }

        if (pending is null)
        {
            return 0;
        }

        using IApplication app = FleetUi.Start();
        var allowed = ApprovalDialog.Ask(app, pending.Request.Summary, pending.Request.Tool);
        inbox.Answer(project, pending.Id, allowed ? ApprovalDecision.Allowed : ApprovalDecision.Denied);
        return 0;
    }
}
