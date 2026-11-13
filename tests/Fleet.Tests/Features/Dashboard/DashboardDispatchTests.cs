using Fleet.Features.Dashboard.ShowDashboard;
using Fleet.Features.Dashboard.ShowDashboard.Models;
using Fleet.Shared;
using Fleet.Shared.Keymap.Enums;
using Fleet.Ui;
using Fleet.Ui.Models;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Fleet.Tests.Features.Dashboard;

// Opens the real dashboard on the ansi test driver and feeds it actions the way the
// head (TakeRequest) or the fleet menu (OpenMenu -> ShowMenu) hands them over.
[Collection(ModalDepthCollection.Name)]
public sealed class DashboardDispatchTests
{
    private static readonly TimeSpan GiveUp = TimeSpan.FromSeconds(10);

    private static readonly AgentBoard TwoAgents = new(
        [FleetRow.Plain("api/main"), FleetRow.Plain("api/fix")], 2, [false, false]);

    private sealed record Outcome(IReadOnlyList<(int Tab, int Row)> Asked, IReadOnlyList<string> Shown, bool TimedOut);

    private static IEnumerable<string> Texts(View view) =>
        view.SubViews.SelectMany(Texts).Prepend(view.Text);

    // TakeRequest hands over one request per pump tick; once they are all taken it records
    // the text on screen and asks the dashboard to close.
    private static Outcome Run(IReadOnlyList<Key> keys, params FleetAction[] requests)
    {
        using var app = Application.Create().Init("ansi");
        var pending = new Queue<FleetAction>(requests);
        var asked = new List<(int Tab, int Row)>();
        IReadOnlyList<string> shown = [];
        var timedOut = false;

        Task<string?> ChangeHarness(int tab, int row)
        {
            asked.Add((tab, row));
            return Task.FromResult<string?>(null);
        }

        FleetAction TakeRequest()
        {
            if (pending.Count > 0)
            {
                return pending.Dequeue();
            }

            shown = [.. Texts(app.TopRunnableView!)];
            return FleetAction.Close;
        }

        foreach (var key in keys)
        {
            app.Invoke(() => app.Keyboard.RaiseKeyDownEvent(key));
        }

        app.AddTimeout(GiveUp, () =>
        {
            timedOut = true;
            app.RequestStop();
            return false;
        });

        ShowDashboardView.Show(app, "proj", Keymap.Default, Callbacks(TakeRequest, ChangeHarness));

        return new Outcome(asked, shown, timedOut);
    }

    private static DashboardCallbacks Callbacks(
        Func<FleetAction> takeRequest, Func<int, int, Task<string?>> changeHarness) =>
        new(
            LoadRepositories: () => Task.FromResult<IReadOnlyList<RepositoryChoice>>([]),
            AddRepository: () => Task.FromResult<string?>(null),
            ShowMenu: () => FleetAction.ChangeHarness,
            EditKeybinds: () => Keymap.Default,
            ReloadKeymap: () => Keymap.Default,
            EditSettings: () => { },
            ShowLogs: () => { },
            BrowseFiles: () => null,
            RebuildDashboard: () => null,
            TakeRequest: takeRequest,
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
        var run = Run([Key.CursorDown], FleetAction.ChangeHarness);

        Assert.False(run.TimedOut);
        Assert.Equal([(DashboardTabs.AgentsTab, 1)], run.Asked);
    }

    [Fact]
    public void Change_harness_chosen_in_the_fleet_menu_asks_for_the_selected_agents_harness()
    {
        var run = Run([Key.CursorDown], FleetAction.OpenMenu);

        Assert.False(run.TimedOut);
        Assert.Equal([(DashboardTabs.AgentsTab, 1)], run.Asked);
    }

    [Fact]
    public void Change_harness_on_the_repositories_tab_asks_for_no_agent_and_says_to_pick_one()
    {
        var run = Run([], FleetAction.NextTab, FleetAction.NextTab, FleetAction.ChangeHarness);

        Assert.False(run.TimedOut);
        Assert.Empty(run.Asked);
        Assert.Contains("pick an agent on the agents or subs tab first.", run.Shown);
    }
}
