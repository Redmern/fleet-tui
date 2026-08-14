using Fleet.Features.Repositories.AddRepository.Models;
using Fleet.Ui;
using Fleet.Ui.Constants;
using Terminal.Gui.App;
using Terminal.Gui.Views;

namespace Fleet.Features.Repositories.AddRepository;

public static class AddRepositoryView
{
    public const string PickUrl = "like...";

    public static AddRepositoryCommand? Show(
        IApplication app, string projectRoot, IReadOnlyList<string> knownUrls, Keymap keymap)
    {
        AddRepositoryCommand? result = null;

        var window = FleetTheme.Overlay("Add repository");

        var clone = FleetTheme.Toggle(1, 1, "Clone from a URL instead of creating a new repository");
        var nameField = FleetTheme.Field(11, 3);
        var urlField = FleetTheme.Field(11, 5);
        var branchField = FleetTheme.Field(11, 9, "main");

        var like = FleetTheme.Choice(11, 6, PickUrl);

        like.Visible = knownUrls.Count > 0;
        urlField.Enabled = false;
        like.Enabled = false;

        void Cloning(bool on)
        {
            urlField.Enabled = on;
            like.Enabled = on;
        }

        clone.ValueChanged += (_, _) => Cloning(clone.Value == CheckState.Checked);

        like.Accepting += (_, e) =>
        {
            var picked = FleetPicker.Choose(app, "Start from", knownUrls, keymap);

            if (picked is not null)
            {
                urlField.Text = knownUrls[picked.Value];
                urlField.SetFocus();
                urlField.MoveEnd();
            }

            e.Handled = true;
        };

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
            like,
            FleetTheme.Caption(1, 9, "Branch:"),
            branchField,
            FleetTheme.HintBar(FleetHints.AddRepository));

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

        return result;
    }
}
