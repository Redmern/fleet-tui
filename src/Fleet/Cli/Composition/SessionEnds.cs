using Fleet.Features.Orchestrations.ReportStatus;
using Fleet.Features.Orchestrations.ReportStatus.Models;
using Fleet.Ports.Agents;
using Fleet.Ports.Projects;
using Fleet.Ports.Projects.Models;
using Fleet.Ports.Todos;
using Fleet.Shared;
using Fleet.Shared.Hooks;

namespace Fleet.Cli.Composition;

public sealed class SessionEnds(
    IAgentStore store,
    IProjectStore projects,
    Func<Project, OwnerNotice, CancellationToken, Task<bool>> typeToOwner,
    Func<DateTimeOffset> clock,
    ITicketStatusSink? tickets,
    TimeSpan? deadline = null)
{
    public static readonly TimeSpan NoticeDeadline = TimeSpan.FromSeconds(5);

    public async Task<bool> HandleAsync(HookEvent hook, CancellationToken ct = default)
    {
        if (!HookStatus.EndsForGood(hook)
            || store.FindByWorktree(hook.Cwd) is not { } found
            || projects.List().FirstOrDefault(p => ProjectName.Sanitize(p.Name) == found.SessionKey)
                is not { } project)
        {
            return false;
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(deadline ?? NoticeDeadline);

        try
        {
            return await new ReportStatusHandler(
                    store, clock, tickets: tickets, typeToOwner: (notice, token) => typeToOwner(project, notice, token))
                .SessionEndedAsync(project.Name, project.Root, found.Record, limit.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return true;
        }
    }
}
