using Fleet.Features.Dashboard.ShowDashboard;
using Fleet.Features.Dashboard.ShowDashboard.Models;
using Fleet.Shared;
using Fleet.Shared.Keymap.Enums;
using Fleet.Ui;
using Fleet.Ui.Models;
using Terminal.Gui.App;

namespace Fleet.Tests.Features.Dashboard;

// Opens the real dashboard on the ansi test driver and feeds it one action the way the
// head (TakeRequest) or the fleet menu (OpenMenu -> ShowMenu) hands it over.
[Collection(ModalDepthCollection.Name)]
public sealed class DashboardDispatchTests
{
    private static readonly TimeSpan GiveUp = TimeSpan.FromSeconds(10);

    private static readonly AgentBoard TwoAgents = new(
        [FleetRow.Plain("api/main"), FleetRow.Plain("api/fix")], 2, [false, false]);

    private sealed class Requests(params FleetAction[] actions)
    {
        private readonly Queue<FleetAction> _queue = new(actions);

        public FleetAction Take()
        {
            lock (_queue)
            {
                return _queue.Count > 0 ? _queue.Dequeue() : FleetAction.None;
            }
        }
    }

    private static (int Tab, int Row)? Run(
        FleetAction request, FleetAction fromMenu, Func<int, int, Task<string?>>? changeHarness = null)
    {
        using var app = Application.Create().Init("ansi");
        var requests = new Requests(request);
        (int Tab, int Row)? asked = null;

        changeHarness ??= (tab, row) =>
        {
            asked = (tab, row);
            app.Invoke(() => app.RequestStop());
            return Task.FromResult<string?>(null);
        };

        app.AddTimeout(GiveUp, () =>
        {
            app.RequestStop();
            return false;
        });

        ShowDashboardView.Show(app, "proj", Keymap.Default, Callbacks(requests, fromMenu, changeHarness));

        return asked;
    }

    private static DashboardCallbacks Callbacks(
        Requests requests, FleetAction fromMenu, Func<int, int, Task<string?>> changeHarness) =>
        new(
            LoadRepositories: () => Task.FromResult<IReadOnlyList<RepositoryChoice>>([]),
            AddRepository: () => Task.FromResult<string?>(null),
            ShowMenu: () => fromMenu,
            EditKeybinds: () => Keymap.Default,
            ReloadKeymap: () => Keymap.Default,
            EditSettings: () => { },
            ShowLogs: () => { },
            BrowseFiles: () => null,
            RebuildDashboard: () => null,
            TakeRequest: requests.Take,
            LoadAgents: () => TwoAgents,
            LoadSubs: () => SubBoard.Empty,
            NewAgent: (_, _) => Task.FromResult<string?>(null),
            DispatchSub: () => Task.FromResult<string?>(null),
            OpenAgent: (_, _) => Task.FromResult<string?>(null),
            ManageAgent: (_, _) => Task.FromResult<string?>(null),
            HideAgent: (_, _) => null,
            BatchAgents: (_, _, _) => Task.FromResult<string?>(null),
            RemoveRepository: _ => Task.FromResult<string?>(null),
            PullRepository: _ => Task.FromResult<string?>(null),
            ManageRepository: _ => Task.FromResult(new RepositoryManaged(null)),
            OpenRepository: _ => Task.FromResult<string?>(null),
            AgentState: _ => new BranchState(0, 0, false),
            RepositoryState: _ => new BranchState(0, 0, false),
            TakeApproval: () => null,
            AnswerApproval: (_, _) => { },
            Heartbeat: () => { },
            DrainSignals: () => { },
            ChangeHarness: changeHarness);

    [Fact]
    public void A_change_harness_request_from_the_head_asks_for_the_selected_agents_harness()
    {
        Assert.Equal((DashboardTabs.AgentsTab, 0), Run(FleetAction.ChangeHarness, FleetAction.None));
    }

    [Fact]
    public void Change_harness_chosen_in_the_fleet_menu_asks_for_the_selected_agents_harness()
    {
        Assert.Equal((DashboardTabs.AgentsTab, 0), Run(FleetAction.OpenMenu, FleetAction.ChangeHarness));
    }
}
