using System.Collections.ObjectModel;
using Fleet.Features.Dashboard.ShowDashboard.Models;
using Fleet.Shared.Keymap.Enums;
using Fleet.Ui;
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

        var repoHeader = FleetTheme.SectionHeader(1, 0, "Repositories");
        var repoList = FleetTheme.Rows(1, 1, Dim.Percent(45));

        var agentHeader = FleetTheme.SectionHeader(1, Pos.Bottom(repoList) + 1, "Agents");
        var agentList = FleetTheme.Rows(1, Pos.Bottom(agentHeader), Dim.Fill(3));
        agentList.SetSource(new ObservableCollection<string>(
            ["(no agents - spawning agents arrives in phase 2)"]));

        var status = FleetTheme.Caption(1, Pos.AnchorEnd(2), string.Empty);

        FleetKeys.ApplyMotions(repoList, keymap);
        FleetKeys.ApplyMotions(agentList, keymap);

        async Task RefreshAsync()
        {
            var repositories = await callbacks.LoadRepositories().ConfigureAwait(true);
            var rows = DashboardRows.ForRepositories(repositories).Select(r => r.Text).ToList();
            repoList.SetSource(new ObservableCollection<string>(rows));
        }

        var busy = false;

        async Task AddAsync()
        {
            busy = true;

            try
            {
                var error = await callbacks.AddRepository().ConfigureAwait(true);

                if (error is not null)
                {
                    FleetDialog.Error(app, "Could not add repository", error);
                }

                await RefreshAsync().ConfigureAwait(true);
            }
            finally
            {
                busy = false;
            }
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

        void Dispatch(FleetAction action)
        {
            switch (action)
            {
                case FleetAction.Close:
                    app.RequestStop(window);
                    break;

                case FleetAction.AddRepository:
                    _ = AddAsync();
                    break;

                case FleetAction.Refresh:
                    _ = RefreshAsync();
                    break;

                case FleetAction.EditKeybinds:
                    EditKeybinds();
                    break;

                case FleetAction.OpenMenu:
                    Dispatch(callbacks.ShowMenu());
                    break;
            }
        }

        void Keys(object? sender, Key key)
        {
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
                    Dispatch(result.Action);
                }

                return;
            }

            var direct = DashboardKeys.For(key, keymap);

            if (direct.Consume)
            {
                key.Handled = true;
                Dispatch(direct.Action);
            }
        }

        bool Pump()
        {
            if (!busy)
            {
                var pending = callbacks.TakeRequest();

                if (pending != FleetAction.None)
                {
                    Dispatch(pending);
                }
            }

            return true;
        }

        repoList.KeyDown += Keys;
        agentList.KeyDown += Keys;

        app.AddTimeout(TimeSpan.FromMilliseconds(200), Pump);

        window.Add(
            repoHeader,
            repoList,
            agentHeader,
            agentList,
            status,
            FleetTheme.HintBar(FleetHintText.Dashboard(keymap)));

        _ = RefreshAsync();

        try
        {
            app.Run(window);
        }
        finally
        {
            window.Dispose();
        }
    }
}
