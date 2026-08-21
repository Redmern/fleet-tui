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

    public static AddRepositoryCommand? Show(
        IApplication app, string projectRoot, IReadOnlyList<string> knownUrls, Keymap keymap)
    {
        AddRepositoryCommand? result = null;

        var cloning = false;
        var name = string.Empty;
        var url = string.Empty;
        var branch = "main";

        var window = FleetTheme.Overlay("Add repository");
        var list = FleetTheme.Rows(1, 1, Dim.Fill(2));
        var rows = new List<(FleetRow Row, Action Act)>();

        void EditName()
        {
            var next = FleetPrompt.Text(app, "Repository name", name, "Repository name", allowEmpty: true);

            if (next is not null)
            {
                name = next;
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

            var typed = FleetPrompt.Text(app, "Clone URL", url, "Clone URL", allowEmpty: true);

            if (typed is not null)
            {
                url = typed;
            }
        }

        void EditBranch()
        {
            var next = FleetPrompt.Text(app, "Branch", branch, "Branch", allowEmpty: true);

            if (next is not null)
            {
                branch = next;
            }
        }

        void Submit()
        {
            result = cloning
                ? AddRepositoryCommand.CloneFrom(projectRoot, name, url, branch)
                : AddRepositoryCommand.CreateNew(projectRoot, name, branch);

            app.RequestStop(window);
        }

        void Rebuild()
        {
            rows.Clear();
            rows.Add((Field("Clone", cloning ? "from a URL" : "new repository"), () => cloning = !cloning));
            rows.Add((Field("Name", name.Length == 0 ? "(required)" : name), EditName));

            if (cloning)
            {
                rows.Add((Field("URL", url.Length == 0 ? "(required)" : url), EditUrl));
            }

            rows.Add((Field("Branch", branch.Length == 0 ? "(default)" : branch), EditBranch));
            rows.Add((Choice("Add repository"), Submit));
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

        window.Add(list, FleetTheme.HintBar(FleetHints.AddRepository));

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
        new([new FleetSpan($"{label}:".PadRight(8), FleetTones.Key), FleetSpan.Plain(value)]);

    private static FleetRow Choice(string label) =>
        new(
        [
            new FleetSpan(FleetGlyphs.PillLeft, FleetTones.ChipEdge),
            new FleetSpan($" {label} ", FleetTones.ChipLabel),
            new FleetSpan(FleetGlyphs.PillRight, FleetTones.ChipEdge),
        ]);
}
