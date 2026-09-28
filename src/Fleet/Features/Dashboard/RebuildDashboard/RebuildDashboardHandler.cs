using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Enums;
using Fleet.Ports.Mux.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;
using Fleet.Shared.Results;

namespace Fleet.Features.Dashboard.RebuildDashboard;

public sealed class RebuildDashboardHandler(IMuxDriver mux)
{
    public async Task<Result<string>> HandleAsync(
        string projectRoot,
        IReadOnlyList<AgentRecord> agents,
        string harness,
        CancellationToken ct = default)
    {
        var panes = await mux.ListPanesAsync(ct).ConfigureAwait(false);
        var self = panes.FirstOrDefault(p => p.Id == mux.CurrentPane);

        if (self is null)
        {
            return Result<string>.Fail(
                "fleet cannot find its own pane; the dashboard has to run inside wezterm.");
        }

        var mates = panes.Where(p => p.TabId == self.TabId && p.Id != self.Id).ToList();
        var harnessPane = HarnessPane(panes, self, projectRoot, agents);

        if (harnessPane is not null && harnessPane.TabId == self.TabId && mates.Count == 1)
        {
            await mux.SetTitleAsync(self.Id, FleetTabTitles.Dashboard, ct).ConfigureAwait(false);

            return Result<string>.Ok("the dashboard layout is intact.");
        }

        if (harnessPane is not null)
        {
            await mux.SplitAsync(
                    new SplitOptions(harnessPane.Id, SplitDirection.Right)
                    {
                        Percent = 50,
                        MovePane = self.Id,
                    },
                    ct)
                .ConfigureAwait(false);
        }
        else
        {
            if (mates.Count > 0)
            {
                await mux.MovePaneAsync(
                        self.Id, new MovePaneOptions { WindowId = self.WindowId }, ct)
                    .ConfigureAwait(false);
            }

            await mux.SplitAsync(
                    new SplitOptions(self.Id, SplitDirection.Left)
                    {
                        Percent = 50,
                        Cwd = projectRoot,
                        Args = AgentHarness.CommandFor(harness),
                    },
                    ct)
                .ConfigureAwait(false);
        }

        await mux.SetTitleAsync(self.Id, FleetTabTitles.Dashboard, ct).ConfigureAwait(false);
        await mux.FocusPaneAsync(self.Id, ct).ConfigureAwait(false);

        var strays = mates.Count(m => m.Id != harnessPane?.Id);

        return Result<string>.Ok(strays == 0
            ? "dashboard rebuilt."
            : $"dashboard rebuilt; {strays} stray pane{(strays == 1 ? string.Empty : "s")} left in the old tab.");
    }

    private static Pane? HarnessPane(
        IReadOnlyList<Pane> panes, Pane self, string projectRoot, IReadOnlyList<AgentRecord> agents)
    {
        var candidates = panes
            .Where(p => p.Id != self.Id
                && p.WindowId == self.WindowId
                && !agents.Any(a => AgentPaneMatch.Owns(p, a)))
            .ToList();

        return candidates.FirstOrDefault(p =>
                string.Equals(p.Title, FleetTabTitles.Dashboard, StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault(p => PathKey.Same(p.Cwd, projectRoot));
    }
}
