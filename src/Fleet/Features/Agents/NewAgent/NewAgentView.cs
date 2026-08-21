using Fleet.Features.Agents.NewAgent.Models;
using Fleet.Shared.Constants;
using Fleet.Ui;
using Fleet.Ui.Constants;
using Fleet.Ui.Models;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;

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
        var harness = AgentHarness.Normalize(prompt.Harness);
        var branch = string.Empty;

        var window = FleetTheme.Overlay("New agent");
        var list = FleetTheme.Rows(1, 1, Dim.Fill(3));
        var rows = new List<(FleetRow Row, Action Act)>();

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
            chosenBase = prompt.Repositories[repository].DefaultBranch;
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

            if (picked is not null)
            {
                chosenBase = branches[picked.Value].Reference;
            }
        }

        void ChooseHarness()
        {
            var picked = FleetPicker.Choose(
                app,
                "Opens",
                AgentHarness.All.Select(AgentHarness.Describe).ToList(),
                keymap,
                AgentHarness.All.ToList().IndexOf(harness));

            if (picked is not null)
            {
                harness = AgentHarness.All[picked.Value];
            }
        }

        void EditBranch()
        {
            var next = FleetPrompt.Text(
                app, "Branch name", branch, "Branch name (empty works on the base)", allowEmpty: true);

            if (next is not null)
            {
                branch = next;
            }
        }

        void Submit()
        {
            result = new NewAgentCommand(
                prompt.ProjectName,
                prompt.Repositories[repository].Name,
                prompt.Repositories[repository].Directory,
                branch,
                chosenBase,
                harness);

            app.RequestStop(window);
        }

        void Rebuild()
        {
            rows.Clear();
            rows.Add((Field("Repo", prompt.Repositories[repository].Name), ChooseRepository));
            rows.Add((Field("Branch", branch.Length == 0 ? "(base itself)" : branch), EditBranch));
            rows.Add((Field("Base", Shown(chosenBase)), ChooseBase));
            rows.Add((Field("Opens", AgentHarness.Describe(harness)), ChooseHarness));
            rows.Add((Choice("Create agent"), Submit));
            rows.Add((Choice("Cancel"), () => app.RequestStop(window)));
        }

        void Refill(int selected)
        {
            Rebuild();
            FleetRows.Fill(list, [.. rows.Select(r => r.Row)], selected);
        }

        Refill(0);
        FleetKeys.ApplyMotions(list, keymap);

        void Activate()
        {
            var index = FleetRows.Selected(list);

            if (index < 0 || index >= rows.Count)
            {
                return;
            }

            rows[index].Act();
            Refill(index);
        }

        list.Accepting += (_, e) =>
        {
            Activate();
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
            list,
            FleetTheme.Caption(
                1,
                Pos.AnchorEnd(2),
                "Empty branch works on the base; empty base cuts from the default branch."),
            FleetTheme.HintBar(FleetHints.NewAgent));

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

    private static FleetRow Field(string label, string value) =>
        new([new FleetSpan($"{label}:".PadRight(9), FleetTones.Key), FleetSpan.Plain(value)]);

    private static FleetRow Choice(string label) => FleetRow.Plain(label);

    private static string Shown(string branch) =>
        branch.Length == 0 ? DefaultBase : branch;
}
