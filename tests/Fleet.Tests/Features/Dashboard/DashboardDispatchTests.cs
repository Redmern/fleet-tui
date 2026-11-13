using Fleet.Features.Dashboard.ShowDashboard;
using Fleet.Features.Dashboard.ShowDashboard.Models;
using Fleet.Shared;
using Fleet.Shared.Keymap.Enums;
using Fleet.Ui;
using Fleet.Ui.Models;
using Terminal.Gui.App;
using Terminal.Gui.Input;

namespace Fleet.Tests.Features.Dashboard;

// Opens the real dashboard on the ansi test driver and feeds it actions the way the
// head (TakeRequest) or the fleet menu (OpenMenu -> ShowMenu) hands them over.
// The requests end with Close, so a run ends once the dashboard has taken them all.
[Collection(ModalDepthCollection.Name)]
public sealed class DashboardDispatchTests
{
    private static readonly TimeSpan GiveUp = TimeSpan.FromSeconds(10);

    private static readonly AgentBoard TwoAgents = new(
        [FleetRow.Plain("api/main"), FleetRow.Plain("api/fix")], 2, [false, false]);

    private sealed class Requests(IEnumerable<FleetAction> actions)
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

    private static List<(int Tab, int Row)> Run(IReadOnlyList<Key> keys, params FleetAction[] requests)
    {
        using var app = Application.Create().Init("ansi");
        var asked = new List<(int Tab, int Row)>();

        Task<string?> ChangeHarness(int tab, int row)
        {
            asked.Add((tab, row));
            return Task.FromResult<string?>(null);
        }

        foreach (var key in keys)
        {
            app.Invoke(() => app.Keyboard.RaiseKeyDownEvent(key));
        }

        app.AddTimeout(GiveUp, () =>
        {
            app.RequestStop();
            return false;
        });

        ShowDashboardView.Show(
            app,
            "proj",
            Keymap.Default,
            Callbacks(new Requests([.. requests, FleetAction.Close]), FleetAction.ChangeHarness, ChangeHarness));

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
            LoadAgentsFast: () => TwoAgents,
            ChangeHarness: changeHarness);

    [Fact]
    public void A_change_harness_request_from_the_head_asks_for_the_selected_agents_harness()
    {
        Assert.Equal([(DashboardTabs.AgentsTab, 1)], Run([Key.CursorDown], FleetAction.ChangeHarness));
    }

    [Fact]
    public void Change_harness_chosen_in_the_fleet_menu_asks_for_the_selected_agents_harness()
    {
        Assert.Equal([(DashboardTabs.AgentsTab, 1)], Run([Key.CursorDown], FleetAction.OpenMenu));
    }

    [Fact]
    public void Change_harness_on_the_repositories_tab_asks_for_no_agent()
    {
        Assert.Empty(Run([], FleetAction.NextTab, FleetAction.NextTab, FleetAction.ChangeHarness));
    }
}
