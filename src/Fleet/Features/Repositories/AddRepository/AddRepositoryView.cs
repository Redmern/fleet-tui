using Fleet.Features.Repositories.AddRepository.Models;
using Fleet.Ui;
using Fleet.Ui.Constants;
using Fleet.Ui.Models;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;

namespace Fleet.Features.Repositories.AddRepository;

public static class AddRepositoryView
{
    public const string PickUrl = "like...";

    public const string Title = $"{FleetIcons.AddRepo} Add repository";

    public static AddRepositoryCommand? Show(
        IApplication app, string projectRoot, IReadOnlyList<string> knownUrls, Keymap keymap)
    {
        AddRepositoryCommand? result = null;

        var cloning = false;
        var name = string.Empty;
        var url = string.Empty;
        var branch = "main";

        var window = FleetTheme.Overlay(Title);
        var list = FleetTheme.Rows(1, 1, Dim.Fill(2));
        var rows = new List<(FleetRow Row, Action Act)>();

        void EditName()
        {
            if (FleetPrompt.Ask(app, "Repository name", name, "Repository name", allowEmpty: true) is { } next)
            {
                name = next.Value;
                SubmitWhen(next);
            }
        }

        void EditUrl()
        {
            if (knownUrls.Count > 0)
            {
                var options = knownUrls.Append("Type a URL...").ToList();
                var picked = FleetPicker.Choose(app, "Clone from", options, keymap);

                if (picked is null)
                {
                    return;
                }

                if (picked.Value < knownUrls.Count)
                {
                    url = knownUrls[picked.Value];
                    return;
                }
            }

            if (FleetPrompt.Ask(app, "Clone URL", url, "Clone URL", allowEmpty: true) is { } typed)
            {
                url = typed.Value;
                SubmitWhen(typed);
            }
        }

        void EditBranch()
        {
            if (FleetPrompt.Ask(app, "Branch", branch, "Branch", allowEmpty: true) is { } next)
            {
                branch = next.Value;
                SubmitWhen(next);
            }
        }

        void Submit()
        {
            result = cloning
                ? AddRepositoryCommand.CloneFrom(projectRoot, name, url, branch)
                : AddRepositoryCommand.CreateNew(projectRoot, name, branch);

            app.RequestStop(window);
        }

        void SubmitWhen(FleetPrompt.Reply reply)
        {
            if (reply.SubmitsDialog)
            {
                Submit();
            }
        }

        var buttons = Buttons(Submit, () => app.RequestStop(window));

        void Rebuild()
        {
            Action[] edits = cloning
                ? [() => cloning = !cloning, EditName, EditUrl, EditBranch]
                : [() => cloning = !cloning, EditName, EditBranch];

            rows.Clear();
            rows.AddRange(Fields(cloning, name, url, branch).Zip(edits));
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
            ("bksp", FleetIcons.Back, () =>
            {
                FleetModal.Back();
                app.RequestStop(window);
            }),
        ]);

        window.Add(list, bar.Root);
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

    public static IReadOnlyList<FleetRow> Fields(bool cloning, string name, string url, string branch)
    {
        var rows = new List<FleetRow>
        {
            FleetFieldRow.Of(FleetIcons.Clone, "Clone", cloning ? "from a URL" : "new repository"),
            FleetFieldRow.Of(FleetIcons.RepoName, "Name", Required(name)),
        };

        if (cloning)
        {
            rows.Add(FleetFieldRow.Of(FleetIcons.Url, "URL", Required(url)));
        }

        rows.Add(FleetFieldRow.Of(FleetIcons.Branch, "Branch", branch.Length == 0 ? "(default)" : branch));

        return rows;
    }

    public static IReadOnlyList<FleetPillButton> Buttons(Action add, Action cancel) =>
    [
        new(FleetIcons.AddRepo, "Add repository", "a", add),
        new(FleetIcons.Close, "Cancel", "Esc", cancel),
    ];

    private static string Required(string value) =>
        value.Length == 0 ? "(required)" : value;
}
