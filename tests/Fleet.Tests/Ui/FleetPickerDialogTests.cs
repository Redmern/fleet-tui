using Fleet.Ui;
using Fleet.Ui.Models;
using Terminal.Gui.App;
using Terminal.Gui.Input;

namespace Fleet.Tests.Ui;

// Drives the real picker on the ansi test driver. Each key is its own queued
// invoke; a picker still open after the keys is stopped and reported open.
[Collection(ModalDepthCollection.Name)]
public sealed class FleetPickerDialogTests
{
    private static readonly IReadOnlyList<PickerEntry> Entries =
        [new("alpha", Key: "x"), new("beta", Key: "y"), new("gamma", Key: "z")];

    private static (string? Picked, bool LeftOpen, bool WentBack) Run(FleetPickerOptions? options, params Key[] keys)
    {
        using var app = Application.Create().Init("ansi");
        var leftOpen = false;

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

        var picked = options is null
            ? FleetPicker.Choose(app, "Pick", Entries, Keymap.Default) is { } index ? Entries[index].Label : null
            : FleetPicker.Choose(app, "Pick", Entries, Keymap.Default, options)?.Label;

        return (picked, leftOpen, FleetModal.WentBack());
    }

    [Fact]
    public void A_picker_without_a_filter_still_takes_an_entry_by_its_accelerator()
    {
        Assert.Equal("beta", Run(null, new Key('y')).Picked);
    }

    [Fact]
    public void A_picker_without_a_filter_still_moves_with_j_and_k()
    {
        Assert.Equal("gamma", Run(null, Key.J, Key.J, Key.Enter).Picked);
        Assert.Equal("beta", Run(null, Key.J, Key.J, Key.K, Key.Enter).Picked);
    }

    [Fact]
    public void A_picker_without_a_filter_still_goes_back_on_backspace()
    {
        var (picked, leftOpen, wentBack) = Run(null, Key.Backspace);

        Assert.Null(picked);
        Assert.False(leftOpen);
        Assert.True(wentBack);
    }

    [Fact]
    public void A_filtering_picker_types_the_accelerator_letter_instead_of_taking_it()
    {
        var (picked, leftOpen, _) = Run(new FleetPickerOptions(Filter: true), new Key('y'));

        Assert.Null(picked);
        Assert.True(leftOpen);
    }

    [Fact]
    public void A_filtering_picker_takes_the_first_match_on_enter()
    {
        Assert.Equal("gamma", Run(new FleetPickerOptions(Filter: true), new Key('m'), Key.Enter).Picked);
    }

    [Fact]
    public void Enter_on_no_match_does_nothing()
    {
        var (picked, leftOpen, _) = Run(new FleetPickerOptions(Filter: true), new Key('q'), Key.Enter);

        Assert.Null(picked);
        Assert.True(leftOpen);
    }

    [Fact]
    public void Backspace_in_a_filtering_picker_first_deletes_then_goes_back()
    {
        var filter = new FleetPickerOptions(Filter: true);

        Assert.Equal("alpha", Run(filter, new Key('q'), Key.Backspace, Key.Enter).Picked);
        Assert.True(Run(filter, new Key('q'), Key.Backspace, Key.Backspace).WentBack);
    }
}
