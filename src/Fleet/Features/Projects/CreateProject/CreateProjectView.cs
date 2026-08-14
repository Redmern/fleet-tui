using Fleet.Features.Projects.CreateProject.Enums;
using Fleet.Features.Projects.CreateProject.Models;
using Fleet.Ports.Projects.Models;
using Fleet.Ui;
using Fleet.Ui.Constants;
using Terminal.Gui.App;

namespace Fleet.Features.Projects.CreateProject;

public static class CreateProjectView
{
    public const string PickLabel = "browse";

    public static Project? Show(
        IApplication app, CreateProjectHandler handler, Func<string, string?>? pickFolder = null)
    {
        Project? created = null;

        var window = FleetTheme.Overlay("New project");

        var nameField = FleetTheme.Field(11, 1);
        var rootField = FleetTheme.Field(11, 3);
        var browse = FleetTheme.Choice(11, 4, PickLabel);
        var error = FleetTheme.ErrorText(1, 6);

        browse.Visible = pickFolder is not null;

        browse.Accepting += (_, e) =>
        {
            var chosen = pickFolder?.Invoke(rootField.Text);

            if (chosen is { Length: > 0 })
            {
                rootField.Text = chosen;
                rootField.SetFocus();
                rootField.MoveEnd();
            }
            else
            {
                error.Text = "No folder was chosen.";
            }

            e.Handled = true;
        };

        void Submit()
        {
            var reply = handler.Handle(new CreateProjectCommand(nameField.Text, rootField.Text));

            if (reply.Status == CreateProjectStatus.NeedsRootConfirmation)
            {
                var confirmed = FleetDialog.Confirm(
                    app,
                    "Create directory?",
                    [
                        reply.RootToCreate ?? string.Empty,
                        string.Empty,
                        "does not exist. Create it?",
                        "Any missing parent directories are created too.",
                    ],
                    confirmText: "Create");

                if (!confirmed)
                {
                    error.Text = "Cancelled — the root directory was not created.";
                    return;
                }

                reply = handler.Handle(
                    new CreateProjectCommand(nameField.Text, rootField.Text, CreateRoot: true));
            }

            if (reply.Status != CreateProjectStatus.Created)
            {
                error.Text = reply.Error ?? "Unknown error.";
                return;
            }

            created = reply.Project;
            app.RequestStop(window);
        }

        nameField.Accepting += (_, e) =>
        {
            Submit();
            e.Handled = true;
        };

        rootField.Accepting += (_, e) =>
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

        window.Add(
            FleetTheme.Caption(1, 1, "Name:"),
            nameField,
            FleetTheme.Caption(1, 3, "Root:"),
            rootField,
            browse,
            error,
            FleetTheme.HintBar(FleetHints.Form));

        FleetModal.Enter();

        try
        {
            app.Run(window);
        }
        finally
        {
            FleetModal.Leave();
            window.Dispose();
        }

        return created;
    }
}
