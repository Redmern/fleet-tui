using Fleet.Ui;
using Fleet.Ui.Constants;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace Fleet.Tests.Ui;

// Drives the real prompt on the ansi test driver. Each key is its own queued
// invoke; a prompt still open after the keys is stopped and reported open.
[Collection(ModalDepthCollection.Name)]
public sealed class FleetPromptDialogTests
{
    private sealed record Outcome(
        FleetPrompt.Reply? Reply, bool WentBack, bool LeftOpen, int Width, IReadOnlyList<string> Rows);

    private static Outcome Run(bool allowEmpty, params Key[] keys)
    {
        using var app = Application.Create().Init("ansi");
        var width = 0;
        IReadOnlyList<string> rows = [];
        var leftOpen = false;

        app.Invoke(() =>
        {
            var window = app.TopRunnableView!;
            width = window.Frame.Width;
            rows = [.. window.SubViews.OfType<FleetList>().SelectMany(l => l.Source!.ToList().Cast<string>())];
        });

        foreach (var key in keys)
        {
            app.Invoke(() => app.Keyboard.RaiseKeyDownEvent(key));
        }

        app.Invoke(() =>
        {
            if (app.TopRunnable is { StopRequested: false } open)
            {
                leftOpen = true;
                app.RequestStop(open);
            }
        });

        var reply = FleetPrompt.Ask(app, "Rename", label: "New name", allowEmpty: allowEmpty);

        return new Outcome(reply, FleetModal.WentBack(), leftOpen, width, rows);
    }

    private static Key[] Typed(string text) => [.. text.Select(c => new Key(c))];

    [Fact]
    public void Enter_returns_the_typed_text_trimmed()
    {
        var outcome = Run(false, [.. Typed(" ab "), Key.Enter]);

        Assert.Equal(new FleetPrompt.Reply("ab", false), outcome.Reply);
        Assert.False(outcome.LeftOpen);
    }

    [Fact]
    public void Ctrl_enter_returns_the_text_and_asks_to_submit_the_dialog()
    {
        var outcome = Run(false, [.. Typed("ab"), Key.Enter.WithCtrl]);

        Assert.Equal(new FleetPrompt.Reply("ab", true), outcome.Reply);
    }

    [Fact]
    public void Enter_on_an_empty_field_keeps_the_prompt_open()
    {
        var outcome = Run(false, Key.Enter);

        Assert.Null(outcome.Reply);
        Assert.True(outcome.LeftOpen);
    }

    [Fact]
    public void Enter_on_an_empty_field_returns_empty_when_empty_is_allowed()
    {
        var outcome = Run(true, Key.Enter);

        Assert.Equal(new FleetPrompt.Reply(string.Empty, false), outcome.Reply);
    }

    [Fact]
    public void Esc_cancels_without_going_back()
    {
        var outcome = Run(false, [.. Typed("ab"), Key.Esc]);

        Assert.Null(outcome.Reply);
        Assert.False(outcome.WentBack);
        Assert.False(outcome.LeftOpen);
    }

    [Fact]
    public void Backspace_on_an_empty_field_goes_back()
    {
        var outcome = Run(false, Key.Backspace);

        Assert.Null(outcome.Reply);
        Assert.True(outcome.WentBack);
        Assert.False(outcome.LeftOpen);
    }

    [Fact]
    public void Backspace_with_text_only_deletes()
    {
        var outcome = Run(false, [.. Typed("ab"), Key.Backspace, Key.Enter]);

        Assert.Equal(new FleetPrompt.Reply("a", false), outcome.Reply);
        Assert.False(outcome.WentBack);
    }

    [Fact]
    public void The_prompt_is_a_card_as_wide_as_the_dialogs()
    {
        Assert.Equal(FleetCard.Width, Run(false).Width);
    }

    [Fact]
    public void The_label_is_a_card_row_led_by_the_edit_icon()
    {
        var rows = Run(false).Rows;

        Assert.Contains(rows, r => r.StartsWith(FleetIcons.Edit, StringComparison.Ordinal) && r.Contains("New name"));
    }

    [Fact]
    public void The_card_shows_an_ok_pill_on_enter_and_a_cancel_pill_on_esc()
    {
        var rows = Run(false).Rows;

        Assert.Contains(rows, r => r.Contains($"{FleetIcons.Select} OK") && r.Contains(FleetKeyText.Display("Enter")));
        Assert.Contains(rows, r => r.Contains($"{FleetIcons.Close} Cancel") && r.Contains(FleetKeyText.Display("Esc")));
    }
}
