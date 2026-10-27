using Fleet.Features.Repositories.AddRepository;
using Fleet.Features.Repositories.AddRepository.Models;
using Fleet.Ui;
using Fleet.Ui.Constants;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Fleet.Tests.Features.Repositories;

// Drives the real dialog on the ansi test driver. Each key is its own queued
// invoke, so the name prompt and URL picker the dialog opens take the keys
// that follow, the way a terminal would deliver them.
[Collection(ModalDepthCollection.Name)]
public sealed class AddRepositoryDialogTests
{
    private const string Root = "/projects/demo";

    private static readonly IReadOnlyList<string> Known = ["git@x:fleet.git", "git@x:site.git"];

    private sealed record Outcome(AddRepositoryCommand? Command, bool WentBack, string Error, IReadOnlyList<string> Rows);

    private static Outcome Run(IReadOnlyList<string> known, params Key[] keys)
    {
        using var app = Application.Create().Init("ansi");
        var error = string.Empty;
        IReadOnlyList<string> rows = [];

        Step(app, keys, 0, () =>
        {
            if (app.TopRunnableView is not { } window)
            {
                return;
            }

            error = window.SubViews.OfType<Label>().Single(l => l.SchemeName == FleetSchemes.Error).Text;
            rows = [.. window.SubViews.OfType<FleetList>().Single().Source!.ToList().Cast<string>()];
            app.Keyboard.RaiseKeyDownEvent(Key.Esc);
        });

        var command = AddRepositoryView.Show(app, Root, known, Keymap.Default);

        return new Outcome(command, FleetModal.WentBack(), error, rows);
    }

    // Each key queues the next before it is raised, since a key that opens a
    // prompt runs a nested loop. A key waits while the dialog on top is still
    // closing, so it reaches the dialog underneath.
    private static void Step(IApplication app, IReadOnlyList<Key> keys, int at, Action last) =>
        app.AddTimeout(TimeSpan.FromMilliseconds(1), () =>
        {
            if (app.TopRunnable is { StopRequested: true })
            {
                return true;
            }

            if (at == keys.Count)
            {
                last();
                return false;
            }

            Step(app, keys, at + 1, last);
            app.Keyboard.RaiseKeyDownEvent(keys[at]);
            return false;
        });

    private static Key[] Typed(string text) => [.. text.Select(c => new Key(c))];

    private static Key[] Name(string name) => [new Key('n'), .. Typed(name), Key.Enter];

    [Fact]
    public void A_named_new_repository_is_created_on_ctrl_enter_with_the_main_branch()
    {
        var outcome = Run([], [.. Name("fleet"), Key.Enter.WithCtrl]);

        Assert.Equal(AddRepositoryCommand.CreateNew(Root, "fleet", "main"), outcome.Command);
    }

    [Fact]
    public void The_add_pill_submits_too()
    {
        var outcome = Run([], [.. Name("fleet"), new Key('a')]);

        Assert.Equal(AddRepositoryCommand.CreateNew(Root, "fleet", "main"), outcome.Command);
    }

    [Fact]
    public void Submitting_without_a_name_shows_an_inline_error_and_stays_open()
    {
        var outcome = Run([], Key.Enter.WithCtrl);

        Assert.Null(outcome.Command);
        Assert.False(outcome.WentBack);
        Assert.Equal(AddRepositoryForm.NameMissing, outcome.Error);
    }

    [Fact]
    public void After_the_error_a_name_can_still_be_given_and_submitted()
    {
        var outcome = Run([], [Key.Enter.WithCtrl, .. Name("fleet"), Key.Enter.WithCtrl]);

        Assert.Equal(AddRepositoryCommand.CreateNew(Root, "fleet", "main"), outcome.Command);
    }

    [Fact]
    public void Enter_on_clone_flips_to_cloning_and_shows_the_url_field()
    {
        var outcome = Run([], Key.Enter);

        Assert.Contains(outcome.Rows, r => r.Contains("from a URL"));
        Assert.Contains(outcome.Rows, r => r.Contains("URL:"));
    }

    [Fact]
    public void Cloning_without_a_url_shows_an_inline_error_and_stays_open()
    {
        var outcome = Run([], [Key.Enter, .. Name("fleet"), Key.Enter.WithCtrl]);

        Assert.Null(outcome.Command);
        Assert.Equal(AddRepositoryForm.UrlMissing, outcome.Error);
    }

    [Fact]
    public void A_known_url_can_be_picked_to_clone_from()
    {
        var outcome = Run(Known, [new Key('c'), .. Name("site"), new Key('u'), Key.CursorDown, Key.Enter, Key.Enter.WithCtrl]);

        Assert.Equal(AddRepositoryCommand.CloneFrom(Root, "site", "git@x:site.git", "main"), outcome.Command);
    }

    [Fact]
    public void The_url_suggestions_filter_as_you_type()
    {
        var outcome = Run(Known, [new Key('c'), .. Name("site"), new Key('u'), .. Typed("site"), Key.Enter, Key.Enter.WithCtrl]);

        Assert.Equal(AddRepositoryCommand.CloneFrom(Root, "site", "git@x:site.git", "main"), outcome.Command);
    }

    [Fact]
    public void Type_a_url_asks_for_one()
    {
        var outcome = Run(
            Known,
            [new Key('c'), .. Name("lib"), new Key('u'), Key.CursorDown, Key.CursorDown, Key.Enter, .. Typed("git@y:lib.git"), Key.Enter, Key.Enter.WithCtrl]);

        Assert.Equal(AddRepositoryCommand.CloneFrom(Root, "lib", "git@y:lib.git", "main"), outcome.Command);
    }

    [Fact]
    public void Without_known_urls_the_url_field_asks_straight_away()
    {
        var outcome = Run([], [new Key('c'), .. Name("lib"), new Key('u'), .. Typed("git@y:lib.git"), Key.Enter, Key.Enter.WithCtrl]);

        Assert.Equal(AddRepositoryCommand.CloneFrom(Root, "lib", "git@y:lib.git", "main"), outcome.Command);
    }

    [Fact]
    public void The_branch_can_be_changed_from_main()
    {
        var outcome = Run(
            [],
            [.. Name("fleet"), new Key('b'), Key.Backspace, .. Typed("trunk"), Key.Enter, Key.Enter.WithCtrl]);

        Assert.Equal(AddRepositoryCommand.CreateNew(Root, "fleet", "trunk"), outcome.Command);
    }

    [Fact]
    public void J_moves_down_and_enter_edits_the_field()
    {
        var outcome = Run([], [new Key('j'), Key.Enter, .. Typed("fleet"), Key.Enter, Key.Enter.WithCtrl]);

        Assert.Equal(AddRepositoryCommand.CreateNew(Root, "fleet", "main"), outcome.Command);
    }

    [Fact]
    public void Esc_cancels()
    {
        var outcome = Run([], Name("fleet"));

        Assert.Null(outcome.Command);
        Assert.False(outcome.WentBack);
    }

    [Fact]
    public void Backspace_goes_back()
    {
        using var app = Application.Create().Init("ansi");
        app.Invoke(() => app.Keyboard.RaiseKeyDownEvent(Key.Backspace));

        Assert.Null(AddRepositoryView.Show(app, Root, [], Keymap.Default));
        Assert.True(FleetModal.WentBack());
    }
}
