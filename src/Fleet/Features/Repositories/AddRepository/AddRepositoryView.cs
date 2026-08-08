using Fleet.Features.Repositories.AddRepository.Models;
using Fleet.Ui;
using Fleet.Ui.Constants;
using Terminal.Gui.App;
using Terminal.Gui.Views;

namespace Fleet.Features.Repositories.AddRepository;

public static class AddRepositoryView
{
    public static AddRepositoryCommand? Show(IApplication app, string projectRoot)
    {
        AddRepositoryCommand? result = null;

        var window = FleetTheme.Modal("Add repository", 78, 13);

        var clone = FleetTheme.Toggle(1, 1, "Clone from a URL instead of creating a new repository");
        var nameField = FleetTheme.Field(11, 3);
        var urlField = FleetTheme.Field(11, 5);
        var branchField = FleetTheme.Field(11, 7, "main");

        urlField.Enabled = false;
        clone.ValueChanged += (_, _) => urlField.Enabled = clone.Value == CheckState.Checked;

        void Submit()
        {
            result = clone.Value == CheckState.Checked
                ? AddRepositoryCommand.CloneFrom(
                    projectRoot, nameField.Text, urlField.Text, branchField.Text)
                : AddRepositoryCommand.CreateNew(projectRoot, nameField.Text, branchField.Text);

            app.RequestStop(window);
        }

        foreach (var field in new[] { nameField, urlField, branchField })
        {
            field.Accepting += (_, e) =>
            {
                Submit();
                e.Handled = true;
            };
        }

        window.KeyDown += (_, key) =>
        {
            if (key == FleetKeys.Cancel)
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
            FleetTheme.HintBar(FleetHints.AddRepository));

        app.Run(window);
        window.Dispose();

        return result;
    }
}
