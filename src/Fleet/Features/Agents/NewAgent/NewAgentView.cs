using Fleet.Features.Agents.NewAgent.Models;
using Fleet.Ui;
using Fleet.Ui.Constants;
using Terminal.Gui.App;

namespace Fleet.Features.Agents.NewAgent;

public static class NewAgentView
{
    public static NewAgentCommand? Show(
        IApplication app,
        string projectName,
        string repositoryName,
        string repositoryDirectory,
        string harness)
    {
        NewAgentCommand? result = null;

        var window = FleetTheme.Overlay("New agent");

        var branchField = FleetTheme.Field(11, 3);
        var baseField = FleetTheme.Field(11, 5);

        void Submit()
        {
            result = new NewAgentCommand(
                projectName,
                repositoryName,
                repositoryDirectory,
                branchField.Text,
                baseField.Text,
                harness);

            app.RequestStop(window);
        }

        foreach (var field in new[] { branchField, baseField })
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
            FleetTheme.Caption(1, 1, $"Repo:      {repositoryName}"),
            FleetTheme.Caption(1, 3, "Branch:"),
            branchField,
            FleetTheme.Caption(1, 5, "From:"),
            baseField,
            FleetTheme.Caption(1, 7, "Leave From empty to cut from the default branch."),
            FleetTheme.HintBar(FleetHints.NewAgent));

        app.Run(window);
        window.Dispose();

        return result;
    }
}
