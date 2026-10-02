using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Enums;
using Fleet.Ports.Mux.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;
using Fleet.Shared.Results;

namespace Fleet.Features.Agents.OpenEditor;

public sealed class OpenEditorHandler(IMuxDriver mux)
{
    public async Task<Result> HandleAsync(
        string project,
        AgentRecord agent,
        string projectRoot,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(agent.Worktree))
        {
            return Result.Fail($"{agent.Worktree} is gone, so there is nothing to edit.");
        }

        var panes = await mux.ListPanesAsync(ct).ConfigureAwait(false);

        var open = panes.FirstOrDefault(p => AgentPaneMatch.IsEditor(p, agent));

        if (open is not null)
        {
            await mux.FocusPaneAsync(open.Id, ct).ConfigureAwait(false);

            return Result.Ok();
        }

        var title = AgentPaneMatch.EditorTitle(agent);
        var args = AgentHarness.BrowseCommandFor(title);

        var beside = panes.FirstOrDefault(p =>
            AgentPanes.Owns(p, agent) && !SubBrowse.Is(p) && !FleetWorkspaces.IsHidden(p.SessionName));

        var pane = beside is not null
            ? await mux.SplitAsync(
                new SplitOptions(beside.Id, SplitDirection.Right)
                {
                    Percent = 50,
                    Cwd = agent.Worktree,
                    Args = args,
                },
                ct).ConfigureAwait(false)
            : await mux.SpawnAsync(
                new SpawnOptions
                {
                    Cwd = agent.Worktree,
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

    public static AgentRecord? Caller(
        IReadOnlyList<Pane> panes,
        PaneId self,
        bool floating,
        string cwd,
        IReadOnlyList<AgentRecord> agents)
    {
        var menu = panes.FirstOrDefault(p => p.Id == self);

        var below = floating && menu is not null
            ? panes.FirstOrDefault(p =>
                p.WindowId == menu.WindowId && p.TabId != menu.TabId && p.IsActive)
            : null;

        return below is not null
            ? agents.FirstOrDefault(a =>
                AgentPaneMatch.Owns(below, a) || AgentPaneMatch.IsEditor(below, a))
            : agents.FirstOrDefault(a => PathKey.Same(a.Worktree, cwd));
    }
}
