using Fleet.Features.Remotes.CreateRemoteProject.Enums;
using Fleet.Ui;
using Fleet.Ui.Constants;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;

namespace Fleet.Features.Remotes.CreateRemoteProject;

public static class CreateRemoteProjectView
{
    public const string Creating = "creating...";

    public static string? Show(IApplication app, CreateRemoteProjectHandler handler, string host, string label)
    {
        string? created = null;
        var busy = 0;
        var open = true;

        var window = FleetTheme.Overlay($"New project on {label}");

        var nameField = FleetTheme.Field(11, 1);
        var pathField = FleetTheme.Field(11, 3);
        var error = FleetTheme.ErrorText(1, 5);

        void Submit()
        {
            if (Interlocked.Exchange(ref busy, 1) != 0)
            {
                return;
            }

            var name = nameField.Text;
            var path = pathField.Text;
            error.Text = Creating;

            _ = Task.Run(async () =>
            {
                try
                {
                    var reply = await handler.SubmitAsync(
                        host,
                        name,
                        path,
                        directory => FleetAsync.OnUi(app, () => FleetDialog.Confirm(
                            app,
                            "Create directory?",
                            [
                                directory,
                                string.Empty,
                                $"does not exist on {label}. Create it?",
                                "Any missing parent directories are created too.",
                            ],
                            confirmText: "Create"))).ConfigureAwait(false);

                    app.Invoke(() =>
                    {
                        if (!open)
                        {
                            return;
                        }

                        if (reply.Status == RemoteProjectStatus.Created)
                        {
                            created = reply.Project;
                            app.RequestStop(window);
                            return;
                        }

                        error.Text = reply.Error ?? "Unknown error.";
                    });
                }
                finally
                {
                    Interlocked.Exchange(ref busy, 0);
                }
            });
        }

        nameField.Accepting += (_, e) =>
        {
            Submit();
            e.Handled = true;
        };

        pathField.Accepting += (_, e) =>
        {
            Submit();
            e.Handled = true;
        };

        window.KeyDown += (_, key) =>
        {
            if (key == FleetKeys.Cancel)
            {
                app.RequestStop(window);
                key.Handled = true;
            }
        };

        var bar = new FleetActionBar(Pos.AnchorEnd(1));

        bar.Show(
        [
            ("enter", FleetIcons.Select, Submit),
        ]);

        window.Add(
            FleetTheme.Caption(1, 1, "Name:"),
            nameField,
            FleetTheme.Caption(1, 3, "Path:"),
            pathField,
            error,
            bar.Root);
        FleetCorners.Attach(window, () => app.RequestStop(window));

        FleetModal.Enter();

        try
        {
            app.Run(window);
        }
        finally
        {
            open = false;
            FleetModal.Leave();
            window.Dispose();
        }

        return created;
    }
}
