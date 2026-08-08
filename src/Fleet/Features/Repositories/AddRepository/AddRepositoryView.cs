using Fleet.Ui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace Fleet.Features.Repositories.AddRepository;

/// <summary>
/// Collects an add-repository request. Returns null if cancelled.
///
/// Validation is deliberately absent: the handler owns it, so it stays testable
/// without a terminal, and the caller shows whatever error comes back.
/// </summary>
public static class AddRepositoryView
{
    public static AddRepositoryCommand? Show(IApplication app, string projectRoot)
    {
        AddRepositoryCommand? result = null;

        var window = FleetTheme.Modal("Add repository", 78, 15);

        // A CheckBox rather than a radio group: Terminal.Gui v2.4 has no
        // RadioGroup, and the choice is binary anyway.
        var clone = FleetTheme.Toggle(1, 1, "Clone from a _URL instead of creating a new repository");

        var nameField = FleetTheme.Field(11, 3);
        var urlField = FleetTheme.Field(11, 5);
        var branchField = FleetTheme.Field(11, 7, "main");

        urlField.Enabled = false;
        clone.ValueChanged += (_, _) => urlField.Enabled = clone.Value == CheckState.Checked;

        var add = FleetTheme.Primary(1, 9, "_Add");
        var cancel = FleetTheme.Secondary(10, 9, "Cancel");

        add.Accepting += (_, _) =>
        {
            result = clone.Value == CheckState.Checked
                ? AddRepositoryCommand.CloneFrom(
                    projectRoot, nameField.Text, urlField.Text, branchField.Text)
                : AddRepositoryCommand.CreateNew(projectRoot, nameField.Text, branchField.Text);

            app.RequestStop(window);
        };

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
            clone,
            FleetTheme.Caption(1, 3, "Name:"),
            nameField,
            FleetTheme.Caption(1, 5, "URL:"),
            urlField,
            FleetTheme.Caption(1, 7, "Branch:"),
            branchField,
            add,
            cancel,
            FleetTheme.HintBar("space  toggle clone      enter  add      esc  cancel"));

        app.Run(window);
        window.Dispose();

        return result;
    }
}
