using Fleet.Features.Repositories.AddRepository.Models;
using Fleet.Ui;
using Fleet.Ui.Constants;
using Fleet.Ui.Models;
using Terminal.Gui.App;

namespace Fleet.Features.Repositories.AddRepository;

public static class AddRepositoryView
{
    public const string TypeUrl = "Type a URL...";

    public const string Title = $"{FleetIcons.AddRepo} Add repository";

    public static readonly (string Icon, string Label, string Mnemonic) Add =
        (FleetIcons.AddRepo, "Add repository", "a");

    public static readonly FleetPickerOptions UrlPicker = new(Filter: true);

    public static AddRepositoryCommand? Show(
        IApplication app, string projectRoot, IReadOnlyList<string> knownUrls, Keymap keymap)
    {
        var cloning = false;
        var name = string.Empty;
        var url = string.Empty;
        var branch = "main";

        void EditName()
        {
            if (FleetPrompt.Text(app, "Repository name", name, "Repository name", allowEmpty: true) is { } next)
            {
                name = next;
            }
        }

        void EditUrl()
        {
            if (knownUrls.Count > 0)
            {
                var picked = FleetPicker.Choose(
                    app, "Clone from", PickerEntry.Plain([.. knownUrls, TypeUrl]), keymap, UrlPicker);

                if (picked is null)
                {
                    return;
                }

                if (picked.Label != TypeUrl)
                {
                    url = picked.Label;
                    return;
                }
            }

            if (FleetPrompt.Text(app, "Clone URL", url, "Clone URL", allowEmpty: true) is { } typed)
            {
                url = typed;
            }
        }

        void EditBranch()
        {
            if (FleetPrompt.Text(app, "Branch", branch, "Branch", allowEmpty: true) is { } next)
            {
                branch = next;
            }
        }

        var submitted = FleetCard.Show(
            app,
            Title,
            keymap,
            () => Fields(cloning, name, url, branch, () => cloning = !cloning, EditName, EditUrl, EditBranch),
            Add,
            "Enter on Clone switches between a new repository and cloning a URL.",
            () => AddRepositoryForm.Validate(cloning, name, url, branch));

        if (!submitted)
        {
            return null;
        }

        return cloning
            ? AddRepositoryCommand.CloneFrom(projectRoot, name.Trim(), url.Trim(), branch.Trim())
            : AddRepositoryCommand.CreateNew(projectRoot, name.Trim(), branch.Trim());
    }

    public static IReadOnlyList<FleetCardField> Fields(
        bool cloning,
        string name,
        string url,
        string branch,
        Action toggleClone,
        Action editName,
        Action editUrl,
        Action editBranch)
    {
        var fields = new List<FleetCardField>
        {
            new(FleetIcons.Clone, "Clone", cloning ? "from a URL" : "new repository", toggleClone),
            new(FleetIcons.RepoName, "Name", Required(name), editName),
        };

        if (cloning)
        {
            fields.Add(new(FleetIcons.Url, "URL", Required(url), editUrl));
        }

        fields.Add(new(FleetIcons.Branch, "Branch", Required(branch), editBranch));

        return fields;
    }

    private static string Required(string value) =>
        value.Length == 0 ? "(required)" : value;
}
