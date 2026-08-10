using Fleet.Features.Agents.NewAgent.Models;
using Fleet.Shared.Constants;
using Fleet.Ui;
using Fleet.Ui.Constants;
using Terminal.Gui.App;
using Terminal.Gui.Views;

namespace Fleet.Features.Agents.NewAgent;

public static class NewAgentView
{
    public const string DefaultBase = "(default branch)";

    public static NewAgentCommand? Show(
        IApplication app,
        NewAgentPrompt prompt,
        Keymap keymap)
    {
        if (prompt.Repositories.Count == 0)
        {
            FleetDialog.Error(
                app,
                "No repositories",
                "This project has no repositories yet. Add one from the fleet menu first.");

            return null;
        }

        NewAgentCommand? result = null;

        var repository = Math.Clamp(prompt.Selected, 0, prompt.Repositories.Count - 1);
        var chosenBase = prompt.Repositories[repository].DefaultBranch;

        var window = FleetTheme.Overlay("New agent");

        var harness = AgentHarness.Normalize(prompt.Harness);

        var repositoryRow = FleetTheme.Choice(14, 1, prompt.Repositories[repository].Name);
        var branchField = FleetTheme.Field(14, 3);
        var baseRow = FleetTheme.Choice(14, 5, Shown(chosenBase));
        var harnessRow = FleetTheme.Choice(14, 7, AgentHarness.Describe(harness));
        var create = FleetTheme.Submit(1, 12, "Create agent");
        var cancel = FleetTheme.Secondary(18, 12, "Cancel");

        void ChooseHarness()
        {
            var picked = FleetPicker.Choose(
                app,
                "Harness",
                AgentHarness.All.Select(AgentHarness.Describe).ToList(),
                keymap,
                AgentHarness.All.ToList().IndexOf(harness));

            if (picked is null)
            {
                return;
            }

            harness = AgentHarness.All[picked.Value];
            harnessRow.Text = AgentHarness.Describe(harness);
        }

        void ChooseRepository()
        {
            var picked = FleetPicker.Choose(
                app,
                "Repository",
                prompt.Repositories.Select(r => r.Name).ToList(),
                keymap,
                repository);

            if (picked is null)
            {
                return;
            }

            repository = picked.Value;
            repositoryRow.Text = prompt.Repositories[repository].Name;
            chosenBase = prompt.Repositories[repository].DefaultBranch;
            baseRow.Text = Shown(chosenBase);
        }

        void ChooseBase()
        {
            var branches = prompt.Branches(prompt.Repositories[repository].Directory);

            if (branches.Count == 0)
            {
                FleetDialog.Error(
                    app, "No branches", "That repository has no branches to start from.");

                return;
            }

            var picked = FleetPicker.Choose(
                app, "Base branch", branches.Select(b => b.Label).ToList(), keymap);

            if (picked is null)
            {
                return;
            }

            chosenBase = branches[picked.Value].Reference;
            baseRow.Text = Shown(chosenBase);
        }

        void Submit()
        {
            result = new NewAgentCommand(
                prompt.ProjectName,
                prompt.Repositories[repository].Name,
                prompt.Repositories[repository].Directory,
                branchField.Text,
                chosenBase,
                harness);

            app.RequestStop(window);
        }

        repositoryRow.Accepting += (_, e) =>
        {
            ChooseRepository();
            e.Handled = true;
        };

        baseRow.Accepting += (_, e) =>
        {
            ChooseBase();
            e.Handled = true;
        };

        harnessRow.Accepting += (_, e) =>
        {
            ChooseHarness();
            e.Handled = true;
        };

        create.Accepting += (_, e) =>
        {
            Submit();
            e.Handled = true;
        };

        cancel.Accepting += (_, e) =>
        {
            app.RequestStop(window);
            e.Handled = true;
        };

        branchField.Accepting += (_, e) =>
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
            FleetTheme.Caption(1, 1, "Repo:"),
            repositoryRow,
            FleetTheme.Caption(1, 3, "Branch name:"),
            branchField,
            FleetTheme.Caption(1, 5, "Base:"),
            baseRow,
            FleetTheme.Caption(1, 7, "Opens:"),
            harnessRow,
            FleetTheme.Caption(1, 9, "Leave the branch name empty to work on the base itself."),
            FleetTheme.Caption(1, 10, "Leave the base empty to cut from the default branch."),
            create,
            cancel,
            FleetTheme.HintBar(FleetHints.NewAgent));

        app.Run(window);
        window.Dispose();

        return result;
    }

    private static string Shown(string branch) =>
        branch.Length == 0 ? DefaultBase : branch;
}
