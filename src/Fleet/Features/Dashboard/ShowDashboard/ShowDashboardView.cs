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
        IApplication app,
        string projectName,
        Keymap keymap,
        DashboardCallbacks callbacks,
        bool menu = false,
        string? notice = null)
    {
        var window = FleetTheme.Screen(menu ? "menu" : $"fleet — {projectName}");

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

        var tip = FleetTheme.Caption(0, 0, string.Empty);
        tip.Visible = false;

        void HideTip()
        {
            if (tip.Visible)
            {
                tip.Visible = false;
                window.SetNeedsDraw();
            }
        }


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
            HideTip();
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

        agentList.MousePositionTracking = true;

        agentList.MouseEvent += (_, m) =>
        {
            if (m.Position is not { } at)
            {
                return;
            }

            var row = agentList.Viewport.Y + at.Y;
            var text = board.StatusAt(row);

            if (text.Length == 0)
            {
                HideTip();
                return;
            }

            tip.Text = $" {text} ";
            tip.X = agentList.Frame.X + Math.Max(0, at.X - 2);
            tip.Y = agentList.Frame.Y + at.Y + 1;
            tip.Visible = true;
            window.SetNeedsDraw();
        };

        agentList.MouseLeave += (_, _) => HideTip();

        HashSet<int>[] marks = [[], [], []];

        IReadOnlyList<FleetRow> Marked(IReadOnlyList<FleetRow> rows, int tab)
        {
            var set = marks[tab];

            if (set.Count == 0)
            {
                return rows;
            }

            return
            [
                .. rows.Select((r, i) => new FleetRow(
                    [
                        new FleetSpan(set.Contains(i) ? "*  " : "   ", FleetTones.Key),
                        .. r.Spans,
                    ],
                    r.Trailing)),
            ];
        }

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

            if (board.Count != agentBoard.Count)
            {
                marks[DashboardTabs.AgentsTab].Clear();
            }

            if (subs.Rows.Count != subBoard.Rows.Count)
            {
                marks[DashboardTabs.SubsTab].Clear();
            }

            board = agentBoard;
            FleetRows.Fill(
                agentList,
                Marked(board.Rows, DashboardTabs.AgentsTab),
                FleetRows.Selected(agentList));
            tabBar.Retitle(DashboardTabs.AgentsTab, DashboardTabs.Agents(board.Count));
            ShowBarFor(DashboardTabs.AgentsTab);

            subs = subBoard;
            FleetRows.Fill(
                subList,
                Marked(subs.Rows, DashboardTabs.SubsTab),
                FleetRows.Selected(subList));
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
        var statusSeen = string.Empty;
        var statusAge = 0;

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

        async Task NewSubAsync()
        {
            busy = true;

            try
            {
                var error = await callbacks.DispatchSub().ConfigureAwait(false);

                app.Invoke(() =>
                {
                    if (error is not null)
                    {
                        FleetDialog.Error(app, "Could not dispatch the sub", error);
                    }

                    ShowTab(DashboardTabs.SubsTab);
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
            else if (menu)
            {
                app.Invoke(() => app.RequestStop(window));
            }
        }

        void ToggleMark()
        {
            var tab = ActiveTab();

            if (tab == DashboardTabs.RepositoriesTab)
            {
                return;
            }

            var row = ActiveRow();
            var count = tab == DashboardTabs.SubsTab ? subs.Rows.Count : board.Count;

            if (row < 0 || row >= count)
            {
                return;
            }

            if (!marks[tab].Add(row))
            {
                marks[tab].Remove(row);
            }

            var list = tab == DashboardTabs.SubsTab ? subList : agentList;
            var rows = tab == DashboardTabs.SubsTab ? subs.Rows : board.Rows;

            FleetRows.Fill(list, Marked(rows, tab), FleetRows.Selected(list));
        }

        void RunBatch(string choice)
        {
            var tab = ActiveTab();
            var indexes = marks[tab].OrderBy(i => i).ToList();

            marks[tab].Clear();
            busy = true;

            Start(async () =>
            {
                try
                {
                    var message = await callbacks.BatchAgents(tab, indexes, choice)
                        .ConfigureAwait(false);

                    app.Invoke(() => status.Text = message ?? string.Empty);

                    await RefreshAsync().ConfigureAwait(false);
                }
                finally
                {
                    busy = false;
                }
            });
        }

        void BatchMenu()
        {
            var picked = FleetPicker.Choose(
                app,
                $"{marks[ActiveTab()].Count} marked",
                ["Hide or show them", "Stop them", "Remove them, keep their files"],
                keys);

            if (picked is null)
            {
                return;
            }

            if (picked == 2 && !FleetDialog.Confirm(
                    app, $"Remove {marks[ActiveTab()].Count} marked?", [], "Remove"))
            {
                return;
            }

            RunBatch(picked switch { 0 => "hide", 1 => "stop", _ => "forget" });
        }

        void HideAgent()
        {
            var tab = ActiveTab();
            var row = ActiveRow();

            busy = true;

            Start(async () =>
            {
                try
                {
                    var message = await Task.Run(() => callbacks.HideAgent(tab, row))
                        .ConfigureAwait(false);

                    app.Invoke(() => status.Text = message ?? string.Empty);

                    await RefreshAsync().ConfigureAwait(false);
                }
                finally
                {
                    busy = false;
                }
            });
        }

        void ManageAgent()
        {
            var tab = ActiveTab();
            var row = ActiveRow();

            busy = true;

            Start(async () =>
            {
                try
                {
                    var message = await callbacks.ManageAgent(tab, row).ConfigureAwait(false);

                    app.Invoke(() => status.Text = message ?? string.Empty);

                    await RefreshAsync().ConfigureAwait(false);
                }
                finally
                {
                    busy = false;
                }
            });
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

            busy = true;

            Start(async () =>
            {
                var managed = await callbacks.ManageRepository(chosen).ConfigureAwait(false);

                app.Invoke(() =>
                {
                    status.Text = managed.Status ?? string.Empty;
                    busy = false;

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
                });
            });
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
            else if (menu)
            {
                app.Invoke(() => app.RequestStop(window));
            }
        }

        void RemoveRepository()
        {
            if (repositories.Count == 0)
            {
                return;
            }

            var selected = Math.Clamp(FleetRows.Selected(repoList), 0, repositories.Count - 1);
            var chosen = repositories[selected];

            busy = true;

            Start(async () =>
            {
                try
                {
                    var message = await callbacks.RemoveRepository(chosen).ConfigureAwait(false);

                    app.Invoke(() => status.Text = message ?? string.Empty);

                    await RefreshAsync().ConfigureAwait(false);
                }
                finally
                {
                    busy = false;
                }
            });
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
                    Start(ActiveTab() == DashboardTabs.SubsTab ? NewSubAsync : NewAgentAsync);
                    break;

                case FleetAction.RemoveAgent:
                    if (marks[ActiveTab()].Count > 0)
                    {
                        BatchMenu();
                        break;
                    }

                    ManageAgent();
                    break;

                case FleetAction.ToggleHidden:
                    if (marks[ActiveTab()].Count > 0)
                    {
                        RunBatch("hide");
                        break;
                    }

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

        IReadOnlyList<(string, string, Action)> WithClose(
            IReadOnlyList<(string, string, Action)> bar) =>
            menu ? [.. bar, ("q/esc", "close", () => app.RequestStop(window))] : bar;

        IReadOnlyList<(string, string, Action)> AgentBar() =>
            WithClose(
            [
                (keys.DisplayFor(FleetAction.NewAgent), "add", () => FromKey(FleetAction.NewAgent)),
                ("enter", "open", () => Start(OpenAsync)),
                (keys.DisplayFor(FleetAction.RemoveAgent), "manage",
                    () => FromKey(FleetAction.RemoveAgent)),
                (keys.DisplayFor(FleetAction.ToggleHidden),
                    board.IsHidden(FleetRows.Selected(agentList))
                        ? AgentWords.Show
                        : AgentWords.Hide,
                    () => FromKey(FleetAction.ToggleHidden)),
                (keys.PrefixDisplay, "menu", () => FromKey(FleetAction.OpenMenu)),
            ]);

        IReadOnlyList<(string, string, Action)> SubBar() =>
            WithClose(
            [
                (keys.DisplayFor(FleetAction.NewAgent), "add", () => FromKey(FleetAction.NewAgent)),
                ("enter", "open", () => Start(OpenAsync)),
                (keys.DisplayFor(FleetAction.RemoveAgent), "manage",
                    () => FromKey(FleetAction.RemoveAgent)),
                (keys.DisplayFor(FleetAction.ToggleHidden),
                    subs.IsHidden(FleetRows.Selected(subList))
                        ? AgentWords.Show
                        : AgentWords.Hide,
                    () => FromKey(FleetAction.ToggleHidden)),
                (keys.PrefixDisplay, "menu", () => FromKey(FleetAction.OpenMenu)),
            ]);

        IReadOnlyList<(string, string, Action)> RepositoryBar() =>
            WithClose(
            [
                (keys.DisplayFor(FleetAction.AddRepository), "add",
                    () => FromKey(FleetAction.AddRepository)),
                ("enter", "open", () => Start(OpenRepositoryAsync)),
                (keys.DisplayFor(FleetAction.ManageRepository), "manage",
                    () => FromKey(FleetAction.ManageRepository)),
                (keys.DisplayFor(FleetAction.Refresh), "refresh", () => Start(RefreshAsync)),
                (keys.PrefixDisplay, "menu", () => FromKey(FleetAction.OpenMenu)),
            ]);

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

            if (key == Key.Space)
            {
                ToggleMark();
                key.Handled = true;
                return;
            }

            if (menu && (key == FleetKeys.Cancel || key == keys.KeyFor(FleetAction.Close)))
            {
                key.Handled = true;
                app.RequestStop(window);
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

            if (status.Text != statusSeen)
            {
                statusSeen = status.Text;
                statusAge = 0;
            }
            else if (status.Text.Length > 0 && ++statusAge >= 2)
            {
                status.Text = string.Empty;
                statusSeen = string.Empty;
            }

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
            hints.Root,
            tip);

        ShowTab(DashboardTabs.AgentsTab);

        if (notice is not null)
        {
            status.Text = notice;
        }

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
