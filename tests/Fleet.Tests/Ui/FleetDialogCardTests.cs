using Fleet.Ui;
using Fleet.Ui.Constants;
using Fleet.Ui.Enums;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace Fleet.Tests.Ui;

// Drives the real confirm, choose and error dialogs on the ansi test driver.
// Each key is its own queued invoke; a dialog still open after the keys is
// stopped and reported open.
[Collection(ModalDepthCollection.Name)]
public sealed class FleetDialogCardTests
{
    private sealed record Seen<T>(T Result, bool WentBack, bool LeftOpen, IReadOnlyList<string> Rows);

    private static Seen<T> Run<T>(Func<IApplication, T> open, params Key[] keys)
    {
        using var app = Application.Create().Init("ansi");
        IReadOnlyList<string> rows = [];
        var leftOpen = false;

        app.Invoke(() =>
            rows = [.. app.TopRunnableView!.SubViews.OfType<FleetList>().SelectMany(l => l.Source!.ToList().Cast<string>())]);

        foreach (var key in keys)
        {
            app.Invoke(() => app.Keyboard.RaiseKeyDownEvent(key));
        }

        app.Invoke(() =>
        {
            if (app.TopRunnable is { StopRequested: false } still)
            {
                leftOpen = true;
                app.RequestStop(still);
            }
        });

        var result = open(app);

        return new Seen<T>(result, FleetModal.WentBack(), leftOpen, rows);
    }

    private static Seen<bool> Confirm(params Key[] keys) =>
        Run(app => FleetDialog.Confirm(app, "Remove agent", ["Remove feat/x?"], "Remove"), keys);

    private static Seen<DialogChoice> Choose(params Key[] keys) =>
        Run(app => FleetDialog.Choose(app, "Quit", ["Stop the agents?"], "Stop", "Leave"), keys);

    private static Seen<bool> Error(params Key[] keys) =>
        Run(app =>
        {
            FleetDialog.Error(app, "Failed", "It broke.");
            return true;
        }, keys);

    [Fact]
    public void Y_confirms()
    {
        Assert.True(Confirm(Key.Y).Result);
    }

    [Fact]
    public void N_and_esc_decline()
    {
        Assert.False(Confirm(Key.N).Result);
        Assert.False(Confirm(Key.Esc).Result);
        Assert.False(Confirm(Key.Esc).LeftOpen);
    }

    [Fact]
    public void Enter_confirms_on_the_first_pill()
    {
        var seen = Confirm(Key.Enter);

        Assert.True(seen.Result);
        Assert.False(seen.LeftOpen);
    }

    [Fact]
    public void L_or_right_then_enter_declines_and_h_or_left_goes_back_to_confirm()
    {
        Assert.False(Confirm(Key.L, Key.Enter).Result);
        Assert.False(Confirm(Key.CursorRight, Key.Enter).Result);
        Assert.True(Confirm(Key.L, Key.H, Key.Enter).Result);
        Assert.True(Confirm(Key.CursorRight, Key.CursorLeft, Key.Enter).Result);
    }

    [Fact]
    public void Backspace_goes_back_from_a_confirm()
    {
        var seen = Confirm(Key.Backspace);

        Assert.False(seen.Result);
        Assert.True(seen.WentBack);
    }

    [Fact]
    public void The_confirm_card_shows_its_answers_as_pills_with_key_chips()
    {
        var rows = Confirm().Rows;

        Assert.Contains(rows, r => r.Contains($"{FleetIcons.Select} Remove") && r.Contains(FleetKeyText.Display("y")));
        Assert.Contains(rows, r => r.Contains($"{FleetIcons.Close} No") && r.Contains(FleetKeyText.Display("n")));
    }

    [Fact]
    public void Choose_takes_the_focused_pill_on_enter()
    {
        Assert.Equal(DialogChoice.Primary, Choose(Key.Enter).Result);
        Assert.Equal(DialogChoice.Secondary, Choose(Key.L, Key.Enter).Result);
        Assert.Equal(DialogChoice.Primary, Choose(Key.CursorRight, Key.CursorLeft, Key.Enter).Result);
    }

    [Fact]
    public void Choose_cancels_on_esc_and_goes_back_on_backspace()
    {
        Assert.Equal(DialogChoice.Cancelled, Choose(Key.Esc).Result);

        var back = Choose(Key.Backspace);
        Assert.Equal(DialogChoice.Cancelled, back.Result);
        Assert.True(back.WentBack);
    }

    [Fact]
    public void The_choose_card_shows_both_answers_as_pills()
    {
        var rows = Choose().Rows;

        Assert.Contains(rows, r => r.Contains($"{FleetIcons.Select} Stop"));
        Assert.Contains(rows, r => r.Contains($"{FleetIcons.Close} Leave"));
    }

    [Fact]
    public void An_error_is_dismissed_by_enter_or_esc()
    {
        Assert.False(Error(Key.Enter).LeftOpen);
        Assert.False(Error(Key.Esc).LeftOpen);
    }

    [Fact]
    public void Backspace_goes_back_from_an_error()
    {
        Assert.True(Error(Key.Backspace).WentBack);
    }

    [Fact]
    public void A_letter_does_not_dismiss_an_error()
    {
        Assert.True(Error(Key.X).LeftOpen);
    }

    [Fact]
    public void The_error_card_shows_a_dismiss_pill_on_enter()
    {
        var rows = Error().Rows;

        Assert.Contains(rows, r => r.Contains($"{FleetIcons.Dismiss} Dismiss") && r.Contains(FleetKeyText.Display("Enter")));
    }
}
