using System.Collections.ObjectModel;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
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
        var window = new Window
        {
            Title = $"fleet - {projectName}",
            BorderStyle = LineStyle.Rounded,
        };

        var repoFrame = new FrameView
        {
            Title = "Repositories",
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Percent(45),
        };

        var repoList = new ListView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };

        repoFrame.Add(repoList);

        var agentFrame = new FrameView
        {
            Title = "Agents",
            X = 0,
            Y = Pos.Bottom(repoFrame),
            Width = Dim.Fill(),
            Height = Dim.Fill(2),
        };

        agentFrame.Add(new Label
        {
            X = 1,
            Y = 1,
            Text = "No agents yet - spawning agents arrives in phase 2.",
        });

        var addButton = new Button { X = 1, Y = Pos.AnchorEnd(2), Text = "Add repository" };

        var hint = new Label
        {
            X = 1,
            Y = Pos.AnchorEnd(1),
            Text = "a add    r refresh    q quit",
        };

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

        addButton.Accepting += (_, _) => _ = AddAsync();

        window.KeyDown += (_, key) =>
        {
            if (key == Key.Q)
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

        window.Add(repoFrame, agentFrame, addButton, hint);

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
