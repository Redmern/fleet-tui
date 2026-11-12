using Fleet.Features.Agents;
using Fleet.Features.Agents.ListAgents;
using Fleet.Features.Agents.TellAgent;
using Fleet.Features.Head.ServeHead;
using Fleet.Features.Orchestrations.ReportStatus;
using Fleet.Features.Orchestrations.ReportStatus.Models;
using Fleet.Ports;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux;
using Fleet.Shared;
using Fleet.Shared.Constants;
using Fleet.Shared.Status;

namespace Fleet.Cli.Composition;

public sealed class OwnerNotices(
    string project,
    string root,
    IMuxDriver mux,
    IAgentStore store,
    IFleetLog log,
    Func<bool> mainInNvim)
{
    private readonly TellAgentHandler _teller = new(mux);

    public async Task<bool> TypeAsync(OwnerNotice notice, CancellationToken ct = default)
    {
        var panes = await mux.ListPanesAsync(ct).ConfigureAwait(false);
        var owner = notice.Owner ?? MainRecord();
        var pane = notice.Owner is null
            ? MainPane.Find(panes, root, project, Adapters.DashPane(project))
            : panes.FirstOrDefault(p => AgentPanes.Owns(p, owner) && !SubBrowse.Is(p));

        if (pane is null)
        {
            log.Write(LogTag.For(project, $"could not reach {notice.Name} with: {notice.Message}"));
            return false;
        }

        await _teller.NotifyAsync(owner, pane.Id, notice.Message, ct).ConfigureAwait(false);
        log.Write(LogTag.For(project, $"typed into {notice.Name}: {notice.Message}"));

        return true;
    }

    public void WentIdle(AgentRecord agent)
    {
        var line = $"{agent.Repository}/{agent.Branch}: idle — no pane output for "
                   + $"{(int)PaneQuiet.QuietAfter.TotalSeconds}s and no done report";

        if (new ReportStatusHandler(store).NoticeFor(project, root, agent, line) is not { } notice)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await TypeAsync(notice).ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                log.Write(LogTag.For(project, $"idle notice for {agent.Repository}/{agent.Branch} failed: {e.Message}"));
            }
        });
    }

    private AgentRecord MainRecord() =>
        new(root, string.Empty, string.Empty, AgentHarness.Orchestrator, string.Empty, false, InNvim: mainInNvim());
}
