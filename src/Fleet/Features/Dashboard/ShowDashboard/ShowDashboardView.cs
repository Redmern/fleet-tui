using System.Collections.ObjectModel;
using Fleet.Features.Dashboard.ShowDashboard.Models;
using Fleet.Shared.Keymap.Enums;
using Fleet.Ui;
using Fleet.Ui.Constants;
using Fleet.Ui.Enums;
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
        var prefix = new PrefixRecognizer(keymap);

        var tabBar = FleetTheme.TabBar(1, 0,
            [DashboardTabs.Agents(0), DashboardTabs.Repositories(0)]);

        var agentList = FleetTheme.Rows(1, Pos.Bottom(tabBar.Root), Dim.Fill(2));
        var repoList = FleetTheme.Rows(1, Pos.Bottom(tabBar.Root), Dim.Fill(2));

        var lists = new[] { agentList, repoList };

        var status = FleetTheme.Caption(1, Pos.AnchorEnd(2), string.Empty);
        var hints = FleetTheme.HintBar(FleetHintText.Agents(keymap));

        FleetKeys.ApplyMotions(agentList, keymap);
        FleetKeys.ApplyMotions(repoList, keymap);

        void ShowTab(int index)
        {
            tabBar.Select(index);

            for (var i = 0; i < lists.Length; i++)
            {
                lists[i].Visible = i == index;
            }

            hints.Text = index == DashboardTabs.RepositoriesTab
                ? FleetHintText.Repositories(keymap)
                : FleetHintText.Agents(keymap);

            lists[index].SetFocus();
            window.SetNeedsDraw();
        }

        void RefreshAgents()
        {
            var agents = callbacks.LoadAgents();
            var selected = agentList.SelectedItem ?? 0;

            agentList.SetSource(new ObservableCollection<string>(agents.Rows.ToList()));
            agentList.SelectedItem = Math.Clamp(selected, 0, Math.Max(0, agents.Rows.Count - 1));

            tabBar.Retitle(DashboardTabs.AgentsTab, DashboardTabs.Agents(agents.Count));
        }

        IReadOnlyList<RepositoryChoice> repositories = [];
        var rows = new ObservableCollection<string>();

        void ApplyRepositories(IReadOnlyList<RepositoryChoice> loaded)
        {
            repositories = loaded;

            rows = new ObservableCollection<string>(DashboardRows
                .ForRepositories(loaded.Select(r => (r.Name, r.DefaultBranch)).ToList())
                .Select(r => r.Text));

            repoList.SetSource(rows);
            tabBar.Retitle(DashboardTabs.RepositoriesTab, DashboardTabs.Repositories(loaded.Count));

            RefreshAgents();
        }

        async Task RefreshAsync()
        {
            var loaded = await callbacks.LoadRepositories().ConfigureAwait(false);

            app.Invoke(() => ApplyRepositories(loaded));
        }

        var busy = false;
        var queued = FleetAction.None;

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

            var selected = Math.Clamp(repoList.SelectedItem ?? 0, 0, repositories.Count - 1);

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

        async Task OpenAsync()
        {
            var error = await callbacks.OpenAgent(agentList.SelectedItem ?? -1)
                .ConfigureAwait(false);

            if (error is not null)
            {
                app.Invoke(() => status.Text = error);
            }
        }

        void ManageAgent()
        {
            busy = true;

            try
            {
                var error = callbacks.ManageAgent(agentList.SelectedItem ?? -1);

                status.Text = error ?? string.Empty;
                RefreshAgents();
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

            return repositories[Math.Clamp(repoList.SelectedItem ?? 0, 0, repositories.Count - 1)];
        }

        async Task PullAsync()
        {
            var chosen = Highlighted();

            if (chosen is null)
            {
                return;
            }

            var row = Math.Clamp(repoList.SelectedItem ?? 0, 0, repositories.Count - 1);
            var tick = 0;
            var pulling = true;

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
            if (repoList.Source is not null && row < repoList.Source.Count)
            {
                rows[row] = text;
                repoList.SetNeedsDraw();
            }
        }

        void ManageRepository()
        {
            var chosen = Highlighted();

            if (chosen is null)
            {
                return;
            }

            busy = true;

            try
            {
                status.Text = callbacks.ManageRepository(chosen) ?? string.Empty;
            }
            finally
            {
                busy = false;
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

            var selected = Math.Clamp(repoList.SelectedItem ?? 0, 0, repositories.Count - 1);

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

        void EditKeybinds()
        {
            busy = true;

            try
            {
                callbacks.EditKeybinds();
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
            if (busy)
            {
                return;
            }

            var result = prefix.Feed(key);

            if (result.Handled)
            {
                key.Handled = true;

                status.Text = result.Outcome switch
                {
                    PrefixOutcome.Armed => $"{keymap.PrefixDisplay} ...",
                    _ => string.Empty,
                };

                if (result.Outcome == PrefixOutcome.Action)
                {
                    FromKey(result.Action);
                }

                return;
            }

            var direct = DashboardKeys.For(key, keymap, tabBar.Selected);

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

            var pending = callbacks.TakeRequest();

            if (pending != FleetAction.None)
            {
                Dispatch(pending);
            }

            return true;
        }

        agentList.Accepting += (_, e) =>
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

        window.Add(
            tabBar.Root,
            agentList,
            repoList,
            status,
            hints);

        ShowTab(DashboardTabs.AgentsTab);

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
