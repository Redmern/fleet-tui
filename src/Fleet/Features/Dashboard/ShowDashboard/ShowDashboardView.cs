using Fleet.Features.Dashboard.ShowDashboard.Constants;
using Fleet.Features.Dashboard.ShowDashboard.Models;
using Fleet.Ports.Approvals.Models;
using Fleet.Shared.Constants;
using Fleet.Shared.Keymap.Enums;
using Fleet.Ui;
using Fleet.Ui.Constants;
using Fleet.Ui.Enums;
using Fleet.Ui.Models;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Fleet.Features.Dashboard.ShowDashboard;

public static class ShowDashboardView
{
    public static void Show(
        IApplication app, string projectName, Keymap keymap, DashboardCallbacks callbacks)
    {
        var window = FleetTheme.Screen($"fleet — {projectName}");

        var keys = keymap;
        var prefix = new PrefixRecognizer(keys);

        var tabBar = FleetTheme.TabBar(1, 0,
            [DashboardTabs.Agents(0), DashboardTabs.Subs(0), DashboardTabs.Repositories(0)]);

        var agentList = FleetTheme.Rows(1, Pos.Bottom(tabBar.Root), Dim.Fill(2));
        var subList = FleetTheme.Rows(1, Pos.Bottom(tabBar.Root), Dim.Fill(2));
        var repoList = FleetTheme.Rows(1, Pos.Bottom(tabBar.Root), Dim.Fill(2));

        var lists = new[] { agentList, subList, repoList };

        var status = FleetTheme.StatusLine(Pos.AnchorEnd(2));
        var hints = new FleetActionBar(Pos.AnchorEnd(1));

        FleetKeys.ApplyMotions(agentList, keys);
        FleetKeys.ApplyMotions(subList, keys);
        FleetKeys.ApplyMotions(repoList, keys);

        IReadOnlyList<(string, string, Action)> BarFor(int index) => index switch
        {
            DashboardTabs.RepositoriesTab => RepositoryBar(),
            DashboardTabs.SubsTab => SubBar(),
            _ => AgentBar(),
        };

        void ShowTab(int index)
        {
            tabBar.Select(index);

            for (var i = 0; i < lists.Length; i++)
            {
                lists[i].Visible = i == index;
            }

            hints.Show(BarFor(index));

            lists[index].SetFocus();
            window.SetNeedsDraw();
        }

        var board = new AgentBoard([], 0, []);
        var subs = SubBoard.Empty;

        void ShowBarFor(int tab)
        {
            if (tabBar.Selected == tab)
            {
                hints.Show(BarFor(tab));
            }
        }

        IReadOnlyList<RepositoryChoice> repositories = [];
        var repoRows = new FleetRowSource([]);

        void Bind(
            IReadOnlyList<RepositoryChoice> loaded,
            IReadOnlyList<FleetRow> repoRowsData,
            AgentBoard agentBoard,
            SubBoard subBoard)
        {
            repositories = loaded;
            FleetRows.Fill(repoList, repoRowsData, FleetRows.Selected(repoList));
            repoRows = (FleetRowSource)repoList.Source!;
            tabBar.Retitle(DashboardTabs.RepositoriesTab, DashboardTabs.Repositories(loaded.Count));

            board = agentBoard;
            FleetRows.Fill(agentList, board.Rows, FleetRows.Selected(agentList));
            tabBar.Retitle(DashboardTabs.AgentsTab, DashboardTabs.Agents(board.Count));
            ShowBarFor(DashboardTabs.AgentsTab);

            subs = subBoard;
            FleetRows.Fill(subList, subs.Rows, FleetRows.Selected(subList));
            tabBar.Retitle(DashboardTabs.SubsTab, DashboardTabs.Subs(subs.Count));
            ShowBarFor(DashboardTabs.SubsTab);
        }

        async Task RefreshAsync()
        {
            var loaded = await callbacks.LoadRepositories().ConfigureAwait(false);

            var repoRowsData = DashboardRows.ForRepositories(loaded, callbacks.RepositoryState);
            var agentBoard = callbacks.LoadAgents();
            var subBoard = callbacks.LoadSubs();

            app.Invoke(() => Bind(loaded, repoRowsData, agentBoard, subBoard));
        }

        var busy = false;
        var pulling = false;
        var refreshing = false;
        var queued = FleetAction.None;

        async Task AutoRefreshAsync()
        {
            refreshing = true;

            try
            {
                await RefreshAsync().ConfigureAwait(false);
            }
            finally
            {
                refreshing = false;
            }
        }

        async Task AddAsync()
        {
            busy = true;

            try
            {
                var error = await callbacks.AddRepository().ConfigureAwait(false);

                app.Invoke(() =>
                {
                    if (error is not null)
                    {
                        FleetDialog.Error(app, "Could not add repository", error);
                    }
                });

                await RefreshAsync().ConfigureAwait(false);
            }
            finally
            {
                busy = false;
            }
        }

        async Task NewAgentAsync()
        {
            if (repositories.Count == 0)
            {
                status.Text = DashboardRows.EmptyHint;
                return;
            }

            var selected = Math.Clamp(FleetRows.Selected(repoList), 0, repositories.Count - 1);

            busy = true;

            try
            {
                var error = await callbacks.NewAgent(repositories, selected)
                    .ConfigureAwait(false);

                app.Invoke(() =>
                {
                    if (error is not null)
                    {
                        FleetDialog.Error(app, "Could not start the agent", error);
                    }

                    ShowTab(DashboardTabs.AgentsTab);
                });

                await RefreshAsync().ConfigureAwait(false);
            }
            finally
            {
                busy = false;
            }
        }

        int ActiveTab() => tabBar.Selected;

        int ActiveRow() =>
            FleetRows.Selected(ActiveTab() == DashboardTabs.SubsTab ? subList : agentList);

        async Task OpenAsync()
        {
            var error = await callbacks.OpenAgent(ActiveTab(), ActiveRow())
                .ConfigureAwait(false);

            if (error is not null)
            {
                app.Invoke(() => status.Text = error);
            }
        }

        void HideAgent()
        {
            busy = true;

            try
            {
                status.Text = callbacks.HideAgent(ActiveTab(), ActiveRow()) ?? string.Empty;
                Start(RefreshAsync);
            }
            finally
            {
                busy = false;
            }
        }

        void ManageAgent()
        {
            busy = true;

            try
            {
                var error = callbacks.ManageAgent(ActiveTab(), ActiveRow());

                status.Text = error ?? string.Empty;
                Start(RefreshAsync);
            }
            finally
            {
                busy = false;
            }
        }

        RepositoryChoice? Highlighted()
        {
            if (repositories.Count == 0)
            {
                return null;
            }

            return repositories[Math.Clamp(FleetRows.Selected(repoList), 0, repositories.Count - 1)];
        }

        async Task PullAsync()
        {
            var chosen = Highlighted();

            if (chosen is null)
            {
                return;
            }

            var row = Math.Clamp(FleetRows.Selected(repoList), 0, repositories.Count - 1);
            var tick = 0;

            pulling = true;

            var spin = app.AddTimeout(TimeSpan.FromMilliseconds(90), () =>
            {
                if (!pulling)
                {
                    return false;
                }

                Rewrite(row, $"{FleetGlyphs.Frame(tick++)}  {chosen.Name}   pulling...");

                return true;
            });

            var error = await callbacks.PullRepository(chosen).ConfigureAwait(false);

            pulling = false;

            if (spin is not null)
            {
                app.RemoveTimeout(spin);
            }

            app.Invoke(() =>
            {
                status.Text = error ?? string.Empty;
                Start(RefreshAsync);
            });
        }

        void Rewrite(int row, string text)
        {
            repoRows.Replace(row, FleetRow.Plain(text));
            repoList.SetNeedsDraw();
        }

        void ManageRepository()
        {
            var chosen = Highlighted();

            if (chosen is null)
            {
                return;
            }

            RepositoryManaged managed;

            busy = true;

            try
            {
                managed = callbacks.ManageRepository(chosen);
                status.Text = managed.Status ?? string.Empty;
            }
            finally
            {
                busy = false;
            }

            switch (managed.Follow)
            {
                case FleetAction.PullRepository:
                    Start(PullAsync);
                    return;

                case FleetAction.RemoveRepository:
                    RemoveRepository();
                    return;
            }

            Start(RefreshAsync);
        }

        async Task OpenRepositoryAsync()
        {
            var chosen = Highlighted();

            if (chosen is null)
            {
                return;
            }

            var error = await callbacks.OpenRepository(chosen).ConfigureAwait(false);

            if (error is not null)
            {
                app.Invoke(() => status.Text = error);
            }
        }

        void RemoveRepository()
        {
            if (repositories.Count == 0)
            {
                return;
            }

            var selected = Math.Clamp(FleetRows.Selected(repoList), 0, repositories.Count - 1);

            busy = true;

            try
            {
                var error = callbacks.RemoveRepository(repositories[selected]);

                status.Text = error ?? string.Empty;
            }
            finally
            {
                busy = false;
            }

            Start(RefreshAsync);
        }

        void BrowseFiles()
        {
            busy = true;

            try
            {
                status.Text = callbacks.BrowseFiles() ?? string.Empty;
            }
            finally
            {
                busy = false;
            }
        }

        void ShowLogs()
        {
            busy = true;

            try
            {
                callbacks.ShowLogs();
            }
            finally
            {
                busy = false;
            }
        }

        void UseKeymap(Keymap next)
        {
            if (next.Signature == keys.Signature)
            {
                return;
            }

            keys = next;
            prefix = new PrefixRecognizer(keys);

            FleetKeys.ApplyMotions(agentList, keys);
            FleetKeys.ApplyMotions(subList, keys);
            FleetKeys.ApplyMotions(repoList, keys);

            ShowTab(tabBar.Selected);
        }

        void EditKeybinds()
        {
            busy = true;

            try
            {
                UseKeymap(callbacks.EditKeybinds());
            }
            finally
            {
                busy = false;
            }
        }

        void EditSettings()
        {
            busy = true;

            try
            {
                callbacks.EditSettings();
            }
            finally
            {
                busy = false;
            }
        }

        async Task ReportingAsync(Func<Task> work)
        {
            try
            {
                await work().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                busy = false;
                app.Invoke(() => status.Text = e.Message);
            }
        }

        void Start(Func<Task> work) => _ = ReportingAsync(work);

        void Dispatch(FleetAction action)
        {
            switch (action)
            {
                case FleetAction.Close:
                    app.RequestStop(window);
                    break;

                case FleetAction.AddRepository:
                    Start(AddAsync);
                    break;

                case FleetAction.Refresh:
                    Start(RefreshAsync);
                    break;

                case FleetAction.NewAgent:
                    Start(NewAgentAsync);
                    break;

                case FleetAction.RemoveAgent:
                    ManageAgent();
                    break;

                case FleetAction.ToggleHidden:
                    HideAgent();
                    break;

                case FleetAction.RemoveRepository:
                    RemoveRepository();
                    break;

                case FleetAction.PullRepository:
                    Start(PullAsync);
                    break;

                case FleetAction.ManageRepository:
                    ManageRepository();
                    break;

                case FleetAction.EditKeybinds:
                    EditKeybinds();
                    break;

                case FleetAction.EditSettings:
                    EditSettings();
                    break;

                case FleetAction.ViewLogs:
                    ShowLogs();
                    break;

                case FleetAction.BrowseFiles:
                    BrowseFiles();
                    break;

                case FleetAction.PrevTab:
                    ShowTab(DashboardTabs.Step(tabBar.Selected, -1, tabBar.Count));
                    break;

                case FleetAction.NextTab:
                    ShowTab(DashboardTabs.Step(tabBar.Selected, 1, tabBar.Count));
                    break;

                case FleetAction.OpenMenu:
                    Dispatch(callbacks.ShowMenu());
                    break;
            }
        }

        IReadOnlyList<(string, string, Action)> AgentBar() =>
        [
            (keys.DisplayFor(FleetAction.NewAgent), "add", () => FromKey(FleetAction.NewAgent)),
            ("enter", "open", () => Start(OpenAsync)),
            (keys.DisplayFor(FleetAction.RemoveAgent), "manage",
                () => FromKey(FleetAction.RemoveAgent)),
            (keys.DisplayFor(FleetAction.ToggleHidden),
                board.IsHidden(FleetRows.Selected(agentList)) ? AgentWords.Show : AgentWords.Hide,
                () => FromKey(FleetAction.ToggleHidden)),
            (keys.PrefixDisplay, "menu", () => FromKey(FleetAction.OpenMenu)),
        ];

        IReadOnlyList<(string, string, Action)> SubBar() =>
        [
            ("enter", "open", () => Start(OpenAsync)),
            (keys.DisplayFor(FleetAction.RemoveAgent), "manage",
                () => FromKey(FleetAction.RemoveAgent)),
            (keys.DisplayFor(FleetAction.ToggleHidden),
                subs.IsHidden(FleetRows.Selected(subList)) ? AgentWords.Show : AgentWords.Hide,
                () => FromKey(FleetAction.ToggleHidden)),
            (keys.PrefixDisplay, "menu", () => FromKey(FleetAction.OpenMenu)),
        ];

        IReadOnlyList<(string, string, Action)> RepositoryBar() =>
        [
            (keys.DisplayFor(FleetAction.AddRepository), "add",
                () => FromKey(FleetAction.AddRepository)),
            ("enter", "open", () => Start(OpenRepositoryAsync)),
            (keys.DisplayFor(FleetAction.ManageRepository), "manage",
                () => FromKey(FleetAction.ManageRepository)),
            (keys.DisplayFor(FleetAction.Refresh), "refresh", () => Start(RefreshAsync)),
            (keys.PrefixDisplay, "menu", () => FromKey(FleetAction.OpenMenu)),
        ];

        void FromKey(FleetAction action)
        {
            if (DashboardKeys.OpensAView(action))
            {
                queued = action;
                return;
            }

            Dispatch(action);
        }

        void Keys(object? sender, Key key)
        {
            if (busy || FleetModal.Any)
            {
                return;
            }

            var result = prefix.Feed(key);

            if (result.Handled)
            {
                key.Handled = true;

                status.Text = result.Outcome switch
                {
                    PrefixOutcome.Armed => $"{keys.PrefixDisplay} ...",
                    _ => string.Empty,
                };

                if (result.Outcome == PrefixOutcome.Action)
                {
                    FromKey(result.Action);
                }

                return;
            }

            var direct = DashboardKeys.For(key, keys, tabBar.Selected);

            if (direct.Consume)
            {
                key.Handled = true;
                FromKey(direct.Action);
            }
        }

        bool Pump()
        {
            if (busy)
            {
                return true;
            }

            if (queued != FleetAction.None)
            {
                var action = queued;
                queued = FleetAction.None;
                Dispatch(action);

                return true;
            }

            if (!FleetModal.Any)
            {
                var approval = callbacks.TakeApproval();

                if (approval is not null)
                {
                    ShowApproval(approval);

                    return true;
                }
            }

            var pending = callbacks.TakeRequest();

            if (pending != FleetAction.None)
            {
                Dispatch(pending);
            }

            return true;
        }

        void ShowApproval(PendingApproval approval)
        {
            busy = true;

            try
            {
                var allowed = FleetDialog.Confirm(
                    app, "Approve this action?", ApprovalLines(approval.Request), confirmText: "Allow");

                callbacks.AnswerApproval(approval.Id, allowed);

                status.Text = allowed
                    ? $"allowed {approval.Request.Tool}"
                    : $"declined {approval.Request.Tool}";
            }
            finally
            {
                busy = false;
            }
        }

        static IReadOnlyList<string> ApprovalLines(ApprovalRequest request) =>
            [request.Summary, string.Empty, $"tool: {request.Tool}"];

        bool Beat()
        {
            callbacks.Heartbeat();

            if (!busy && !pulling && !refreshing && queued == FleetAction.None)
            {
                UseKeymap(callbacks.ReloadKeymap());
                Start(AutoRefreshAsync);
            }

            return true;
        }

        agentList.ValueChanged += (_, _) => ShowBarFor(DashboardTabs.AgentsTab);
        subList.ValueChanged += (_, _) => ShowBarFor(DashboardTabs.SubsTab);

        agentList.Accepting += (_, e) =>
        {
            Start(OpenAsync);
            e.Handled = true;
        };

        subList.Accepting += (_, e) =>
        {
            Start(OpenAsync);
            e.Handled = true;
        };

        repoList.Accepting += (_, e) =>
        {
            Start(OpenRepositoryAsync);
            e.Handled = true;
        };

        app.Keyboard.KeyDown += Keys;

        app.AddTimeout(TimeSpan.FromMilliseconds(80), Pump);
        app.AddTimeout(DashboardRefresh.Interval, Beat);

        window.Add(
            tabBar.Root,
            agentList,
            subList,
            repoList,
            status,
            hints.Root);

        ShowTab(DashboardTabs.AgentsTab);

        callbacks.Heartbeat();

        Start(RefreshAsync);

        try
        {
            app.Run(window);
        }
        finally
        {
            app.Keyboard.KeyDown -= Keys;
            window.Dispose();
        }
    }
}
