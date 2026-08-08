using Fleet.Ports.Projects;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
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

        var nameField = new TextField { X = 10, Y = 1, Width = Dim.Fill(2) };
        var rootField = new TextField { X = 10, Y = 3, Width = Dim.Fill(2) };
        var error = new Label { X = 1, Y = 5, Width = Dim.Fill(2), Text = string.Empty };

        var window = new Window
        {
            Title = "New project",
            X = Pos.Center(),
            Y = Pos.Center(),
            Width = 72,
            Height = 11,
            BorderStyle = LineStyle.Rounded,
        };

        var create = new Button { X = 1, Y = 7, Text = "Create", IsDefault = true };
        var cancel = new Button { X = 12, Y = 7, Text = "Cancel" };

        void Submit()
        {
            var outcome = handler.Handle(new CreateProjectCommand(nameField.Text, rootField.Text));

            if (!outcome.Succeeded)
            {
                error.Text = "! " + outcome.Error;
                return;   // stay open so the entry can be corrected
            }

            created = outcome.Value;
            app.RequestStop(window);
        }

        create.Accepting += (_, _) => Submit();
        cancel.Accepting += (_, _) => app.RequestStop(window);

        window.KeyDown += (_, key) =>
        {
            if (key == Key.Esc)
            {
                app.RequestStop(window);
                key.Handled = true;
            }
        };

        window.Add(
            new Label { X = 1, Y = 1, Text = "Name:" },
            nameField,
            new Label { X = 1, Y = 3, Text = "Root:" },
            rootField,
            error,
            create,
            cancel);

        app.Run(window);
        window.Dispose();

        return created;
    }
}
