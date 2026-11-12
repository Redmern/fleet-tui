using System.Collections.Concurrent;
using Fleet.Features.Agents.NewAgent.Models;
using Fleet.Shared.Constants;
using Fleet.Shared.Harness;
using Fleet.Shared.Results;
using Fleet.Ui;
using Fleet.Ui.Constants;
using Fleet.Ui.Models;
using Terminal.Gui.App;

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

        var (repository, harness) = Preselect(prompt, lastRepo, lastHarness);
        var chosenBase = prompt.Repositories[repository].DefaultBranch;
        var branch = string.Empty;
        var hostInNvim = false;

        void ChooseRepository()
        {
            var names = prompt.Repositories.Select(r => r.Name).ToList();
            var picked = FleetPicker.Choose(
                app, "Repository", PickerEntry.Plain(names), keymap, RepositoryPicker, repository);

            if (picked is null)
            {
                return;
            }

            repository = names.IndexOf(picked.Label);
            chosenBase = prompt.Repositories[repository].DefaultBranch;
        }

        void ChooseBase()
        {
            var directory = prompt.Repositories[repository].Directory;
            var branches = prompt.Branches(directory);

            if (branches.Count == 0)
            {
                FleetDialog.Error(
                    app, "No branches", "That repository has no branches to start from.");

                return;
            }

            var known = Known(branches);
            var picked = FleetPicker.Choose(
                app,
                "Base branch",
                [.. branches.Select(b => new PickerEntry(b.Label))],
                keymap,
                BasePicker(prompt.Fetch, directory, known));

            if (picked is not null && known.TryGetValue(picked.Label, out var choice))
            {
                chosenBase = choice.Reference;
            }
        }

        void ChooseHarness()
        {
            var picked = FleetPicker.Choose(
                app,
                "Opens",
                Harnesses.Select(AgentHarness.Describe).ToList(),
                keymap,
                Harnesses.ToList().IndexOf(harness));

            if (picked is not null)
            {
                harness = Harnesses[picked.Value];
                hostInNvim = prompt.NvimAllowed && Hosted(harness, hostInNvim);
            }
        }

        void ToggleHost() => hostInNvim = prompt.NvimAllowed && Toggled(harness, hostInNvim);

        void EditBranch()
        {
            var next = FleetPrompt.Text(
                app, "Branch name", branch, "Branch name (empty works on the base)", allowEmpty: true);

            if (next is not null)
            {
                branch = next;
            }
        }

        var submitted = FleetCard.Show(
            app,
            Title,
            keymap,
            () => Fields(
                prompt.Repositories[repository].Name,
                branch.Length == 0 ? "(base itself)" : branch,
                Shown(chosenBase),
                AgentHarness.Describe(harness),
                prompt.NvimAllowed ? HostShown(harness, hostInNvim) : NvimOff,
                ChooseRepository,
                EditBranch,
                ChooseBase,
                ChooseHarness,
                ToggleHost),
            Create,
            "Empty branch works on the base; empty base cuts from the default branch.");

        return submitted
            ? new NewAgentCommand(
                prompt.ProjectName,
                prompt.Repositories[repository].Name,
                prompt.Repositories[repository].Directory,
                branch,
                chosenBase,
                harness,
                HostInNvim: Hosted(harness, hostInNvim))
            : null;
    }

    public static readonly (string Icon, string Label, string Mnemonic) Create =
        (FleetIcons.Create, "Create agent", "c");

    public static readonly FleetPickerOptions RepositoryPicker = new(Filter: true);

    public static FleetPickerOptions BasePicker(
        Func<string, CancellationToken, Task<Result<IReadOnlyList<BranchChoice>>>>? fetch,
        string directory,
        ConcurrentDictionary<string, BranchChoice> known) =>
        new(
            Filter: true,
            Refresh: fetch is null ? null : async ct =>
            {
                var fetched = await fetch(directory, ct).ConfigureAwait(false);

                if (!fetched.Succeeded)
                {
                    return new PickerRefresh([], fetched.Error ?? string.Empty);
                }

                foreach (var choice in fetched.Value)
                {
                    known.TryAdd(choice.Label, choice);
                }

                return new PickerRefresh([.. fetched.Value.Select(b => new PickerEntry(b.Label))]);
            },
            Refreshing: "fetching origin...");

    public static ConcurrentDictionary<string, BranchChoice> Known(IReadOnlyList<BranchChoice> branches) =>
        new(branches
            .GroupBy(b => b.Label, StringComparer.Ordinal)
            .Select(g => KeyValuePair.Create(g.Key, g.First())));

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
        var harness = Harnesses.Contains(lastHarness)
            ? AgentHarness.HostedFor(lastHarness, prompt.NvimAllowed)
            : fallback;

        return (repository, harness);
    }

    public static IReadOnlyList<FleetCardField> Fields(
        string repository,
        string branch,
        string from,
        string opens,
        string inNvim,
        Action chooseRepository,
        Action editBranch,
        Action chooseBase,
        Action chooseHarness,
        Action toggleHost) =>
    [
        new(FleetIcons.Repo, "Repo", repository, chooseRepository),
        new(FleetIcons.Branch, "Branch", branch, editBranch),
        new(FleetIcons.Base, "Base", from, chooseBase),
        new(FleetIcons.Harness, "Opens", opens, chooseHarness),
        new(FleetIcons.Harness, "In nvim", inNvim, toggleHost),
    ];

    public static IReadOnlyList<string> Harnesses => HarnessRegistry.Selectable;

    public const string NvimOff = "(off: the editor is not nvim)";

    public static bool Hosted(string harness, bool hostInNvim) => hostInNvim && AgentHarness.RunsHarness(harness);

    public static bool Toggled(string harness, bool hostInNvim) => AgentHarness.RunsHarness(harness) && !hostInNvim;

    public static string HostShown(string harness, bool hostInNvim) =>
        !AgentHarness.RunsHarness(harness) ? "(nvim is the editor)" : Hosted(harness, hostInNvim) ? "yes" : "no";

    private static string Shown(string branch) =>
        branch.Length == 0 ? DefaultBase : branch;
}
