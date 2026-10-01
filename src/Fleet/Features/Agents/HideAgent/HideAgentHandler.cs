using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Enums;
using Fleet.Ports.Mux.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;
using Fleet.Shared.Results;

namespace Fleet.Features.Agents.HideAgent;

public sealed class HideAgentHandler(IMuxDriver mux, IAgentStore store)
{
    public async Task<Result<AgentRecord>> HandleAsync(
        string project,
        AgentRecord agent,
        string? dashboardWindow,
        IReadOnlyList<Pane>? knownPanes = null,
        CancellationToken ct = default)
    {
        var hiding = !agent.Hidden;

        var panes = knownPanes ?? await mux.ListPanesAsync(ct).ConfigureAwait(false);
        var active = panes.FirstOrDefault(p => p.IsActive);
        var mine = panes.Where(p => AgentPanes.Owns(p, agent)).ToList();

        var hiddenWindow = hiding && !Workspaces
            ? HiddenNest.WindowOf(panes, store.List(project))
            : null;

        var changed = AgentHarness.IsOrchestrator(agent.Harness)
            ? await ToggleOrchestratorAsync(
                project, agent, dashboardWindow, hiddenWindow, mine, hiding, ct).ConfigureAwait(false)
            : await ToggleAgentAsync(
                project, agent, dashboardWindow, hiddenWindow, mine, hiding, ct).ConfigureAwait(false);

        if (active is not null)
        {
            await mux.FocusPaneAsync(active.Id, ct).ConfigureAwait(false);
        }

        store.Save(project, changed);

        return Result<AgentRecord>.Ok(changed);
    }

    private bool Workspaces => mux.Caps.HasFlag(MuxCaps.Workspaces);

    private async Task HideAsync(
        string project, IEnumerable<PaneId> ids, string? hiddenWindow, CancellationToken ct)
    {
        if (!Workspaces)
        {
            await HiddenNest.MoveIntoAsync(mux, ids, hiddenWindow, ct).ConfigureAwait(false);
            return;
        }

        var into = new MovePaneOptions { Workspace = FleetWorkspaces.HiddenFor(project) };

        foreach (var id in ids)
        {
            await mux.MovePaneAsync(id, into, ct).ConfigureAwait(false);
        }
    }

    private async Task<AgentRecord> ToggleAgentAsync(
        string project,
        AgentRecord agent,
        string? dashboardWindow,
        string? hiddenWindow,
        IReadOnlyList<Pane> mine,
        bool hiding,
        CancellationToken ct)
    {
        if (hiding)
        {
            await HideAsync(project, mine.Select(p => p.Id), hiddenWindow, ct)
                .ConfigureAwait(false);
        }
        else
        {
            var options = new MovePaneOptions
            {
                WindowId = dashboardWindow,
                NewWindow = dashboardWindow is null,
            };

            foreach (var pane in mine)
            {
                await mux.MovePaneAsync(pane.Id, options, ct).ConfigureAwait(false);
            }
        }

        foreach (var pane in mine)
        {
            await mux.SetTitleAsync(
                pane.Id, AgentTitle.For(agent.Repository, agent.Branch), ct).ConfigureAwait(false);
        }

        return agent with { Hidden = hiding, Open = mine.Count > 0 };
    }

    private async Task<AgentRecord> ToggleOrchestratorAsync(
        string project,
        AgentRecord agent,
        string? dashboardWindow,
        string? hiddenWindow,
        IReadOnlyList<Pane> mine,
        bool hiding,
        CancellationToken ct)
    {
        var claude = mine.Where(p => !SubBrowse.Is(p)).ToList();

        if (Workspaces)
        {
            return await ToggleOrchestratorInWorkspacesAsync(
                project, agent, dashboardWindow, mine, claude, hiding, ct).ConfigureAwait(false);
        }

        foreach (var browser in mine.Where(SubBrowse.Is))
        {
            await mux.KillPaneAsync(browser.Id, ct).ConfigureAwait(false);
        }

        if (hiding)
        {
            await HideAsync(project, claude.Select(p => p.Id), hiddenWindow, ct)
                .ConfigureAwait(false);

            foreach (var pane in claude)
            {
                await mux.SetTitleAsync(
                    pane.Id, AgentTitle.For(agent.Repository, agent.Branch), ct)
                    .ConfigureAwait(false);
            }
        }
        else
        {
            foreach (var pane in claude)
            {
                await mux.MovePaneAsync(
                        pane.Id,
                        new MovePaneOptions
                        {
                            WindowId = dashboardWindow,
                            NewWindow = dashboardWindow is null,
                        },
                        ct)
                    .ConfigureAwait(false);
                await mux.SetTitleAsync(
                    pane.Id, AgentTitle.For(agent.Repository, agent.Branch), ct)
                    .ConfigureAwait(false);
            }
        }

        return agent with { Hidden = hiding, Open = claude.Count > 0 };
    }

    private async Task<AgentRecord> ToggleOrchestratorInWorkspacesAsync(
        string project,
        AgentRecord agent,
        string? dashboardWindow,
        IReadOnlyList<Pane> mine,
        IReadOnlyList<Pane> claude,
        bool hiding,
        CancellationToken ct)
    {
        var main = claude.FirstOrDefault();

        if (main is null)
        {
            return agent with { Hidden = hiding, Open = false };
        }

        var into = hiding
            ? new MovePaneOptions { Workspace = FleetWorkspaces.HiddenFor(project) }
            : new MovePaneOptions { WindowId = dashboardWindow, NewWindow = dashboardWindow is null };

        await mux.MovePaneAsync(main.Id, into, ct).ConfigureAwait(false);
        await mux.SetTitleAsync(main.Id, AgentTitle.For(agent.Repository, agent.Branch), ct)
            .ConfigureAwait(false);

        var browser = mine.FirstOrDefault(SubBrowse.Is);

        if (browser is not null)
        {
            await mux.SplitAsync(
                new SplitOptions(main.Id, SplitDirection.Right) { Percent = 50, MovePane = browser.Id },
                ct).ConfigureAwait(false);
        }

        return agent with { Hidden = hiding, Open = true };
    }
}
