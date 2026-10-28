using Fleet.Ui;
using Fleet.Ui.Constants;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Fleet.Tests.Ui;

// Drives the real card on the ansi test driver. Each key is its own queued
// invoke so a nested prompt or picker can take the keys that follow.
[Collection(ModalDepthCollection.Name)]
public sealed class FleetCardDialogTests
{
    private static (bool Submitted, List<string> Errors) Run(
        Func<IReadOnlyList<FleetCardField>> fields, Func<string?>? validate, params Key[] keys)
    {
        using var app = Application.Create().Init("ansi");
        var errors = new List<string>();

        foreach (var key in keys.Append(Key.Esc))
        {
            app.Invoke(() =>
            {
                if (app.TopRunnableView is { } window)
                {
                    errors.Add(Error(window));
                }

                app.Keyboard.RaiseKeyDownEvent(key);
            });
        }

        var submitted = FleetCard.Show(app, "Card", Keymap.Default, fields, (FleetIcons.Create, "Create", "c"), validate: validate);

        return (submitted, errors);
    }

    private static string Error(View window) =>
        window.SubViews.OfType<Label>().SingleOrDefault(l => l.SchemeName == FleetSchemes.Error)?.Text ?? string.Empty;

    private static IReadOnlyList<FleetCardField> One() => [new(FleetIcons.Repo, "Repo", "fleet", () => { })];

    [Fact]
    public void A_failing_check_keeps_the_card_open_and_shows_the_error()
    {
        var (submitted, errors) = Run(One, () => "Name it first.", Key.Enter.WithCtrl);

        Assert.False(submitted);
        Assert.Equal("Name it first.", errors[^1]);
    }

    [Fact]
    public void The_submit_pill_runs_the_check_too()
    {
        var (submitted, errors) = Run(One, () => "Name it first.", new Key('c'));

        Assert.False(submitted);
        Assert.Equal("Name it first.", errors[^1]);
    }

    [Fact]
    public void A_passing_check_submits()
    {
        var (submitted, _) = Run(One, () => null, Key.Enter.WithCtrl);

        Assert.True(submitted);
    }

    [Fact]
    public void Editing_a_field_clears_the_error()
    {
        string? problem = "Name it first.";
        var (submitted, errors) = Run(
            () => [new(FleetIcons.Repo, "Repo", "fleet", () => problem = null)],
            () => problem,
            Key.Enter.WithCtrl,
            new Key('r'));

        Assert.False(submitted);
        Assert.Equal(["", "Name it first.", ""], errors);
    }

    [Fact]
    public void A_field_that_appears_after_an_edit_gets_its_own_jump_key()
    {
        var more = false;
        var ran = new List<string>();

        IReadOnlyList<FleetCardField> Fields() => more
            ? [new(FleetIcons.Repo, "Repo", "x", () => more = false), new(FleetIcons.Url, "URL", "", () => ran.Add("url"))]
            : [new(FleetIcons.Repo, "Repo", "x", () => more = true)];

        Run(Fields, null, new Key('r'), new Key('u'));

        Assert.Equal(["url"], ran);
    }
}
