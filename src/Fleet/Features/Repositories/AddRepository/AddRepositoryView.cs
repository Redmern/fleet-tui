using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
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

        // A CheckBox rather than a radio group: Terminal.Gui v2.4 has no
        // RadioGroup, and the choice is binary anyway.
        var clone = new CheckBox { X = 1, Y = 1, Text = "Clone from a URL" };

        var nameField = new TextField { X = 10, Y = 3, Width = Dim.Fill(2) };
        var urlField = new TextField { X = 10, Y = 5, Width = Dim.Fill(2), Enabled = false };
        var branchField = new TextField { X = 10, Y = 7, Width = Dim.Fill(2), Text = "main" };
        var error = new Label { X = 1, Y = 9, Width = Dim.Fill(2), Text = string.Empty };

        clone.ValueChanged += (_, _) => urlField.Enabled = clone.Value == CheckState.Checked;

        var window = new Window
        {
            Title = "Add repository",
            X = Pos.Center(),
            Y = Pos.Center(),
            Width = 76,
            Height = 15,
            BorderStyle = LineStyle.Rounded,
        };

        var add = new Button { X = 1, Y = 11, Text = "Add", IsDefault = true };
        var cancel = new Button { X = 9, Y = 11, Text = "Cancel" };

        add.Accepting += (_, _) =>
        {
            result = clone.Value == CheckState.Checked
                ? AddRepositoryCommand.CloneFrom(
                    projectRoot, nameField.Text, urlField.Text, branchField.Text)
                : AddRepositoryCommand.CreateNew(projectRoot, nameField.Text, branchField.Text);

            app.RequestStop(window);
        };

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
            clone,
            new Label { X = 1, Y = 3, Text = "Name:" },
            nameField,
            new Label { X = 1, Y = 5, Text = "URL:" },
            urlField,
            new Label { X = 1, Y = 7, Text = "Branch:" },
            branchField,
            error,
            add,
            cancel);

        app.Run(window);
        window.Dispose();

        return result;
    }
}
