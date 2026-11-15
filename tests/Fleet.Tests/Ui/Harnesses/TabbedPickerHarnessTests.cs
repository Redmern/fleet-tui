using Fleet.Ui;
using Fleet.Ui.Models;
using Terminal.Gui.Input;

namespace Fleet.Tests.Ui.Harnesses;

[Collection(ModalDepthCollection.Name)]
public sealed class TabbedPickerHarnessTests
{
    private static readonly IReadOnlyList<(string Title, IReadOnlyList<PickerEntry> Entries)> Tabs =
    [
        ("first", [new PickerEntry("alpha"), new PickerEntry("beta")]),
        ("second", [new PickerEntry("gamma"), new PickerEntry("delta")]),
    ];

    private static HeadlessRun<TabbedPick?> Run(params Key[] keys) =>
        HeadlessUi.Run(
            app => FleetTabbedPicker.Pick(app, "Pick", Tabs, Keymap.Default, [new TabbedAction(Key.D, "drop", "d", 1)]),
            keys);

    [Fact]
    public void Enter_takes_the_selected_entry_of_the_first_tab()
    {
        Assert.Equal(new TabbedPick(0, 0, false), Run(Key.Enter).Result);
    }

    [Fact]
    public void Down_then_enter_takes_the_second_entry()
    {
        Assert.Equal(new TabbedPick(0, 1, false), Run(Key.CursorDown, Key.Enter).Result);
    }

    [Fact]
    public void Right_switches_to_the_second_tab_and_enter_takes_its_first_entry()
    {
        Assert.Equal(new TabbedPick(1, 0, false), Run(Key.CursorRight, Key.Enter).Result);
    }

    [Fact]
    public void Left_from_the_first_tab_wraps_to_the_last()
    {
        Assert.Equal(new TabbedPick(1, 0, false), Run(Key.CursorLeft, Key.Enter).Result);
    }

    [Fact]
    public void Shift_enter_takes_the_entry_for_a_new_window()
    {
        Assert.Equal(new TabbedPick(0, 0, true), Run(Key.Enter.WithShift).Result);
    }

    [Fact]
    public void A_tab_action_key_only_works_on_its_own_tab()
    {
        var onFirst = Run(Key.D);
        var onSecond = Run(Key.CursorRight, Key.D);

        Assert.True(onFirst.LeftOpen);
        Assert.Null(onFirst.Result);
        Assert.Equal("drop", onSecond.Result?.Action?.Hint);
    }

    [Fact]
    public void Backspace_closes_the_picker_and_goes_back()
    {
        var run = Run(Key.Backspace);

        Assert.Null(run.Result);
        Assert.False(run.LeftOpen);
        Assert.True(run.WentBack);
    }

    [Fact]
    public void Escape_closes_the_picker_without_going_back()
    {
        var run = Run(Key.Esc);

        Assert.Null(run.Result);
        Assert.False(run.LeftOpen);
        Assert.False(run.WentBack);
    }
}
