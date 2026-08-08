using Fleet.Ports.Projects;
using Fleet.Ui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Fleet.Features.Projects.CreateProject;

/// <summary>
/// Asks for a project name and root directory. Returns null if cancelled.
///
/// A plain Window rather than a Dialog: Terminal.Gui v2's Accepting event carries
/// a CommandEventArgs with no Cancel, so a Dialog's own button handling cannot be
/// suppressed when validation fails. A Window has no such behaviour to fight — it
/// closes only when this code says so, which is what lets a rejected entry stay on
/// screen with its error showing.
/// </summary>
public static class CreateProjectView
{
    /// <param name="app">
    /// The application instance, passed in rather than reached for statically:
    /// Terminal.Gui v2 marks the static Application facade obsolete.
    /// </param>
    public static Project? Show(IApplication app, CreateProjectHandler handler)
    {
        Project? created = null;

        var window = FleetTheme.Modal("New project", 74, 13);

        var nameField = FleetTheme.Field(11, 1);
        var rootField = FleetTheme.Field(11, 3);
        var error = FleetTheme.ErrorText(1, 5);

        var create = FleetTheme.Primary(1, 7, "_Create");
        var cancel = FleetTheme.Secondary(13, 7, "Cancel");

        void Submit()
        {
            var reply = handler.Handle(new CreateProjectCommand(nameField.Text, rootField.Text));

            // The root does not exist. Creating directories on disk should not
            // happen on a typo, so ask before re-submitting with permission.
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
                return;   // stay open so the entry can be corrected
            }

            created = reply.Project;
            app.RequestStop(window);
        }

        create.Accepting += (_, _) => Submit();
        cancel.Accepting += (_, _) => app.RequestStop(window);

        window.KeyDownNotHandled += (_, key) =>
        {
            if (key == Key.Esc)
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
            create,
            cancel,
            FleetTheme.HintBar("enter  create      esc  cancel      tab  move focus"));

        app.Run(window);
        window.Dispose();

        return created;
    }
}
