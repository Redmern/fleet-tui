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

    public const string Title = $"{FleetIcons.NewAgent} New agent";

    public static NewAgentCommand? Show(
        IApplication app,
        NewAgentPrompt prompt,
        Keymap keymap,
        string lastRepo = "",
        string lastHarness = "")
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

        var (repository, harness) = Preselect(prompt, lastRepo, lastHarness);
        var chosenBase = prompt.Repositories[repository].DefaultBranch;
        var branch = string.Empty;

        var window = FleetTheme.Overlay(Title);
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

        var buttons = Buttons(Submit, () => app.RequestStop(window));

        void Rebuild()
        {
            var fields = Fields(
                prompt.Repositories[repository].Name,
                branch.Length == 0 ? "(base itself)" : branch,
                Shown(chosenBase),
                AgentHarness.Describe(harness));

            rows.Clear();
            rows.Add((fields[0], ChooseRepository));
            rows.Add((fields[1], EditBranch));
            rows.Add((fields[2], ChooseBase));
            rows.Add((fields[3], ChooseHarness));
            rows.AddRange(buttons.Select(b => (b.Row(), b.Activate)));
        }

        void Refill(int selected)
        {
            Rebuild();
            FleetRows.Fill(list, [.. rows.Select(r => r.Row)], selected);
        }

        Refill(0);
        FleetKeys.ApplyMotions(list, keymap);
        FleetPillButton.Bind(list, buttons);

        void Jump(int index, Action act)
        {
            Refill(index);
            act();
            Refill(index);
        }

        var fieldKeys = FieldKeys(
                () => Jump(0, ChooseRepository),
                () => Jump(1, EditBranch),
                () => Jump(3, ChooseHarness))
            .Where(k => !k.Clashes(keymap))
            .ToList();

        FleetPillButton.Bind(list, fieldKeys);

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
            else if (FleetKeys.GoesBack(key))
            {
                FleetModal.Back();
                app.RequestStop(window);
                key.Handled = true;
            }
        };

        var bar = new FleetActionBar(Pos.AnchorEnd(1));

        bar.Show(
        [
            ("enter", FleetIcons.Select, Activate),
            .. fieldKeys.Select(k => (k.Mnemonic, k.Icon, k.Activate)),
            ("bksp", FleetIcons.Back, () =>
            {
                FleetModal.Back();
                app.RequestStop(window);
            }),
        ]);

        window.Add(
            list,
            FleetTheme.Caption(
                1,
                Pos.AnchorEnd(2),
                "Empty branch works on the base; empty base cuts from the default branch."),
            bar.Root);
        FleetCorners.Attach(window, () => app.RequestStop(window));

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

    public static (int Repository, string Harness) Preselect(
        NewAgentPrompt prompt, string lastRepo, string lastHarness)
    {
        var remembered = prompt.Repositories
            .Select((r, index) => (r.Name, Index: index))
            .FirstOrDefault(r => lastRepo.Length > 0 && string.Equals(r.Name, lastRepo, StringComparison.Ordinal));

        var repository = remembered.Name is null
            ? Math.Clamp(prompt.Selected, 0, Math.Max(prompt.Repositories.Count - 1, 0))
            : remembered.Index;

        var fallback = AgentHarness.Normalize(prompt.Harness);
        var harness = AgentHarness.All.Contains(lastHarness)
            ? AgentHarness.HostedFor(lastHarness, AgentHarness.HostedInNvim(fallback))
            : fallback;

        return (repository, harness);
    }

    public static IReadOnlyList<FleetRow> Fields(string repository, string branch, string from, string opens) =>
    [
        FleetFieldRow.Of(FleetIcons.Repo, "Repo", repository),
        FleetFieldRow.Of(FleetIcons.Branch, "Branch", branch),
        FleetFieldRow.Of(FleetIcons.Base, "Base", from),
        FleetFieldRow.Of(FleetIcons.Harness, "Opens", opens),
    ];

    public static IReadOnlyList<FleetPillButton> FieldKeys(Action repository, Action branch, Action opens) =>
    [
        new(FleetIcons.Repo, "Repo", "r", repository),
        new(FleetIcons.Branch, "Branch", "b", branch),
        new(FleetIcons.Harness, "Opens", "o", opens),
    ];

    public static IReadOnlyList<FleetPillButton> Buttons(Action create, Action cancel) =>
    [
        new(FleetIcons.Create, "Create agent", "c", create),
        new(FleetIcons.Close, "Cancel", "Esc", cancel),
    ];

    private static string Shown(string branch) =>
        branch.Length == 0 ? DefaultBase : branch;
}
