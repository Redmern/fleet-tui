using System.Collections.ObjectModel;
using Fleet.Ui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Fleet.Features.Dashboard.ShowDashboard;

/// <param name="LoadRepositories">Re-read the project's repositories.</param>
/// <param name="AddRepository">
/// Returns null on success or cancel, or an error message to display. Supplied by
/// the composition root, so this view depends on no other slice.
/// </param>
public sealed record DashboardCallbacks(
    Func<Task<IReadOnlyList<(string Name, string DefaultBranch)>>> LoadRepositories,
    Func<Task<string?>> AddRepository);

public static class ShowDashboardView
{
    public static void Show(IApplication app, string projectName, DashboardCallbacks callbacks)
    {
        var window = FleetTheme.Screen($"fleet — {projectName}");

        var repoPanel = FleetTheme.Panel("Repositories");
        repoPanel.X = 0;
        repoPanel.Y = 0;
        repoPanel.Width = Dim.Fill();
        repoPanel.Height = Dim.Percent(45);

        var repoList = FleetTheme.Rows();
        repoPanel.Add(repoList);

        var agentPanel = FleetTheme.Panel("Agents");
        agentPanel.X = 0;
        agentPanel.Y = Pos.Bottom(repoPanel);
        agentPanel.Width = Dim.Fill();
        agentPanel.Height = Dim.Fill(4);
        agentPanel.Add(FleetTheme.Caption(1, 1, "No agents yet — spawning agents arrives in phase 2."));

        var add = FleetTheme.Primary(1, Pos.AnchorEnd(3), "_Add repository");
        var refresh = FleetTheme.Secondary(20, Pos.AnchorEnd(3), "_Refresh");
        var quit = FleetTheme.Secondary(33, Pos.AnchorEnd(3), "_Quit");

        async Task RefreshAsync()
        {
            var repositories = await callbacks.LoadRepositories().ConfigureAwait(true);
            var rows = DashboardRows.ForRepositories(repositories).Select(r => r.Text).ToList();
            repoList.SetSource(new ObservableCollection<string>(rows));
        }

        async Task AddAsync()
        {
            var error = await callbacks.AddRepository().ConfigureAwait(true);

            // Creating a repository is destructive-adjacent, so a failure is loud
            // rather than swallowed.
            if (error is not null)
            {
                MessageBox.ErrorQuery(app, "Could not add repository", error, "OK");
            }

            await RefreshAsync().ConfigureAwait(true);
        }

        add.Accepting += (_, _) => _ = AddAsync();
        refresh.Accepting += (_, _) => _ = RefreshAsync();
        quit.Accepting += (_, _) => app.RequestStop(window);

        window.KeyDownNotHandled += (_, key) =>
        {
            if (key == Key.Q || key == Key.Esc)
            {
                app.RequestStop(window);
                key.Handled = true;
            }
            else if (key == Key.A)
            {
                _ = AddAsync();
                key.Handled = true;
            }
            else if (key == Key.R)
            {
                _ = RefreshAsync();
                key.Handled = true;
            }
        };

        window.Add(repoPanel, agentPanel, add, refresh, quit, FleetTheme.HintBar(
            "a  add repository      r  refresh      q  quit      tab  move focus"));

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
