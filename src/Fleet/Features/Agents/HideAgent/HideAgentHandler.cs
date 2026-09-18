using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux;
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
        bool workspaceNative,
        IReadOnlyList<Pane>? knownPanes = null,
        CancellationToken ct = default)
    {
        var hiding = !agent.Hidden;

        var panes = knownPanes ?? await mux.ListPanesAsync(ct).ConfigureAwait(false);
        var active = panes.FirstOrDefault(p => p.IsActive);
        var mine = panes.Where(p => AgentPanes.Owns(p, agent)).ToList();

        var hiddenWindow = hiding && !workspaceNative
            ? HiddenNest.WindowOf(panes, store.List(project))
            : null;

        var changed = AgentHarness.IsOrchestrator(agent.Harness)
            ? await ToggleOrchestratorAsync(
                agent, dashboardWindow, hiddenWindow, workspaceNative, mine, hiding, ct)
                .ConfigureAwait(false)
            : await ToggleAgentAsync(
                agent, dashboardWindow, hiddenWindow, workspaceNative, mine, hiding, ct)
                .ConfigureAwait(false);

        if (active is not null)
        {
            await mux.FocusPaneAsync(active.Id, ct).ConfigureAwait(false);
        }

        store.Save(project, changed);

        return Result<AgentRecord>.Ok(changed);
    }

    private async Task<AgentRecord> ToggleAgentAsync(
        AgentRecord agent,
        string? dashboardWindow,
        string? hiddenWindow,
        bool workspaceNative,
        IReadOnlyList<Pane> mine,
        bool hiding,
        CancellationToken ct)
    {
        if (hiding && workspaceNative && dashboardWindow is not null)
        {
            await HiddenNest.MoveIntoBackgroundAsync(mux, mine.Select(p => p.Id), dashboardWindow, ct)
                .ConfigureAwait(false);
        }
        else if (hiding)
        {
            await HiddenNest.MoveIntoAsync(mux, mine.Select(p => p.Id), hiddenWindow, ct)
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
        AgentRecord agent,
        string? dashboardWindow,
        string? hiddenWindow,
        bool workspaceNative,
        IReadOnlyList<Pane> mine,
        bool hiding,
        CancellationToken ct)
    {
        var claude = mine.Where(p => !SubBrowse.Is(p)).ToList();

        foreach (var browser in mine.Where(SubBrowse.Is))
        {
            await mux.KillPaneAsync(browser.Id, ct).ConfigureAwait(false);
        }

        if (hiding)
        {
            if (workspaceNative && dashboardWindow is not null)
            {
                await HiddenNest.MoveIntoBackgroundAsync(
                    mux, claude.Select(p => p.Id), dashboardWindow, ct).ConfigureAwait(false);
            }
            else
            {
                await HiddenNest.MoveIntoAsync(mux, claude.Select(p => p.Id), hiddenWindow, ct)
                    .ConfigureAwait(false);
            }

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

            if (claude.FirstOrDefault() is { } main)
            {
                await SubBrowse.SplitAsync(mux, agent, main.Id, ct).ConfigureAwait(false);
            }
        }

        return agent with { Hidden = hiding, Open = claude.Count > 0 };
    }
}
