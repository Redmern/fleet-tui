using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Enums;
using Fleet.Ports.Mux.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;
using Fleet.Shared.Results;

namespace Fleet.Features.Agents.MoveProject;

public sealed class MoveProjectHandler(IMuxDriver mux)
{
    public async Task<Result> HandleAsync(
        string project,
        string root,
        IReadOnlyList<AgentRecord> agents,
        string? destWindow,
        string? dashPaneId,
        string? selfPaneId,
        string fleetExecutable,
        CancellationToken ct = default)
    {
        var panes = await mux.ListPanesAsync(ct).ConfigureAwait(false);

        var rootPanes = panes
            .Where(p => PathKey.Same(p.Cwd, root) && p.Id.Value != selfPaneId)
            .ToList();

        if (rootPanes.Count == 0)
        {
            return Result.Fail($"{project} is not open anywhere.");
        }

        var dash = rootPanes.FirstOrDefault(p => p.Id.Value == dashPaneId);
        var claude = dash is null ? null : rootPanes.FirstOrDefault(p => p.Id != dash.Id);

        PaneId anchor;

        if (dash is not null && claude is not null)
        {
            await mux.KillPaneAsync(dash.Id, ct).ConfigureAwait(false);

            await mux.MovePaneAsync(
                    claude.Id,
                    new MovePaneOptions { WindowId = destWindow, NewWindow = destWindow is null },
                    ct)
                .ConfigureAwait(false);

            anchor = claude.Id;
        }
        else
        {
            foreach (var pane in rootPanes)
            {
                await mux.KillPaneAsync(pane.Id, ct).ConfigureAwait(false);
            }

            anchor = await mux.SpawnAsync(
                new SpawnOptions
                {
                    Cwd = root,
                    SessionName = project,
                    WindowId = destWindow,
                    NewWindow = destWindow is null,
                    Args = [AgentHarness.Claude, AgentHarness.ResumeArgument],
                    Env = AgentHarness.SessionPersistence,
                },
                ct).ConfigureAwait(false);

            if (anchor.IsNone)
            {
                return Result.Fail(
                    $"the {mux.Name} multiplexer did not respond. Run 'fleet doctor'.");
            }
        }

        await mux.SetTitleAsync(anchor, FleetTabTitles.Dashboard, ct).ConfigureAwait(false);

        var after = await mux.ListPanesAsync(ct).ConfigureAwait(false);
        var landed = after.FirstOrDefault(p => p.Id == anchor)?.WindowId ?? destWindow;

        var dashPane = await mux.SplitAsync(
            new SplitOptions(anchor, SplitDirection.Right)
            {
                Percent = 50,
                Cwd = root,
                Args = [fleetExecutable, "dash", "--project", project],
            },
            ct).ConfigureAwait(false);

        foreach (var agent in agents.Where(a => a.Open && !a.Hidden))
        {
            await MoveAgentAsync(agent, after, landed, selfPaneId, ct).ConfigureAwait(false);
        }

        if (!dashPane.IsNone)
        {
            await mux.FocusPaneAsync(dashPane, ct).ConfigureAwait(false);
        }

        return Result.Ok();
    }

    private async Task MoveAgentAsync(
        AgentRecord agent,
        IReadOnlyList<Pane> panes,
        string? destWindow,
        string? selfPaneId,
        CancellationToken ct)
    {
        var mine = panes
            .Where(p => AgentPanes.Owns(p, agent)
                && p.Id.Value != selfPaneId
                && !string.Equals(
                    p.SessionName, FleetWorkspaces.Hidden, StringComparison.OrdinalIgnoreCase)
                && p.WindowId != destWindow)
            .ToList();

        if (mine.Count == 0)
        {
            return;
        }

        var options = new MovePaneOptions { WindowId = destWindow, NewWindow = destWindow is null };

        if (AgentHarness.IsOrchestrator(agent.Harness))
        {
            foreach (var browser in mine.Where(SubBrowse.Is))
            {
                await mux.KillPaneAsync(browser.Id, ct).ConfigureAwait(false);
            }

            var main = mine.FirstOrDefault(p => !SubBrowse.Is(p));

            if (main is null)
            {
                return;
            }

            await mux.MovePaneAsync(main.Id, options, ct).ConfigureAwait(false);
            await mux.SetTitleAsync(
                main.Id, AgentTitle.For(agent.Repository, agent.Branch), ct).ConfigureAwait(false);
            await SubBrowse.SplitAsync(mux, agent, main.Id, ct).ConfigureAwait(false);

            return;
        }

        foreach (var pane in mine)
        {
            await mux.MovePaneAsync(pane.Id, options, ct).ConfigureAwait(false);
            await mux.SetTitleAsync(
                pane.Id, AgentTitle.For(agent.Repository, agent.Branch), ct).ConfigureAwait(false);
        }
    }
}
