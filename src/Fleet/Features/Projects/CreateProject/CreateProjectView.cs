using Fleet.Features.Projects.CreateProject.Enums;
using Fleet.Features.Projects.CreateProject.Models;
using Fleet.Ports.Projects.Models;
using Fleet.Ui;
using Fleet.Ui.Constants;
using Terminal.Gui.App;
using Terminal.Gui.Views;

namespace Fleet.Features.Projects.CreateProject;

public static class CreateProjectView
{
    public static Project? Show(IApplication app, CreateProjectHandler handler)
    {
        Project? created = null;

        var window = FleetTheme.Modal("New project", 74, 11);

        var nameField = FleetTheme.Field(11, 1);
        var rootField = FleetTheme.Field(11, 3);
        var error = FleetTheme.ErrorText(1, 5);

        void Submit()
        {
            var reply = handler.Handle(new CreateProjectCommand(nameField.Text, rootField.Text));

            if (reply.Status == CreateProjectStatus.NeedsRootConfirmation)
            {
                var answer = MessageBox.Query(
                    app,
                    "Create directory?",
                    $"{reply.RootToCreate}\n\ndoes not exist. Create it?\n"
                        + "Any missing parent directories are created too.",
                    "No",
                    "Yes");

                if (answer != 1)
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
            error,
            FleetTheme.HintBar(FleetHints.Form));

        app.Run(window);
        window.Dispose();

        return created;
    }
}
