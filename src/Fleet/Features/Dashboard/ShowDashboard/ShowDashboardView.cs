using System.Collections.ObjectModel;
using Fleet.Features.Dashboard.ShowDashboard.Enums;
using Fleet.Features.Dashboard.ShowDashboard.Models;
using Fleet.Ui;
using Fleet.Ui.Constants;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Fleet.Features.Dashboard.ShowDashboard;

public static class ShowDashboardView
{
    public static void Show(IApplication app, string projectName, DashboardCallbacks callbacks)
    {
        var window = FleetTheme.Screen($"fleet — {projectName}");

        var repoHeader = FleetTheme.SectionHeader(1, 0, "Repositories");
        var repoList = FleetTheme.Rows(1, 1, Dim.Percent(45));

        var agentHeader = FleetTheme.SectionHeader(1, Pos.Bottom(repoList) + 1, "Agents");
        var agentList = FleetTheme.Rows(1, Pos.Bottom(agentHeader), Dim.Fill(2));
        agentList.SetSource(new ObservableCollection<string>(
            ["(no agents - spawning agents arrives in phase 2)"]));

        FleetKeys.ApplyMotions(repoList);
        FleetKeys.ApplyMotions(agentList);

        async Task RefreshAsync()
        {
            var repositories = await callbacks.LoadRepositories().ConfigureAwait(true);
            var rows = DashboardRows.ForRepositories(repositories).Select(r => r.Text).ToList();
            repoList.SetSource(new ObservableCollection<string>(rows));
        }

        async Task AddAsync()
        {
            var error = await callbacks.AddRepository().ConfigureAwait(true);

            if (error is not null)
            {
                FleetDialog.Error(app, "Could not add repository", error);
            }

            await RefreshAsync().ConfigureAwait(true);
        }

        void Keys(object? sender, Terminal.Gui.Input.Key key)
        {
            switch (DashboardKeys.For(key))
            {
                case DashboardAction.Quit:
                    app.RequestStop(window);
                    key.Handled = true;
                    break;

                case DashboardAction.Add:
                    _ = AddAsync();
                    key.Handled = true;
                    break;

                case DashboardAction.Refresh:
                    _ = RefreshAsync();
                    key.Handled = true;
                    break;
            }
        }

        repoList.KeyDown += Keys;
        agentList.KeyDown += Keys;

        window.Add(
            repoHeader,
            repoList,
            agentHeader,
            agentList,
            FleetTheme.HintBar(FleetHints.Dashboard));

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
