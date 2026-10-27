using Fleet.Ui;
using Fleet.Ui.Models;
using Terminal.Gui.Input;

namespace Fleet.Tests.Ui;

public class PickerFilterTests
{
    private static IReadOnlyList<PickerEntry> Entries(params string[] labels) => PickerEntry.Plain(labels);

    [Fact]
    public void An_empty_filter_keeps_every_entry_in_order()
    {
        Assert.Equal([0, 1, 2], PickerFilter.Apply(Entries("main", "develop", "origin/x"), string.Empty));
    }

    [Fact]
    public void The_filter_matches_a_substring_anywhere_in_the_label()
    {
        Assert.Equal([1, 2], PickerFilter.Apply(Entries("main", "feat/login", "origin/feat/menu"), "feat"));
    }

    [Fact]
    public void The_filter_ignores_case()
    {
        Assert.Equal([0], PickerFilter.Apply(Entries("Feature/LOGIN", "main"), "login"));
    }

    [Fact]
    public void A_filter_that_matches_nothing_gives_no_entries()
    {
        Assert.Empty(PickerFilter.Apply(Entries("main", "develop"), "zzz"));
    }

    [Fact]
    public void The_first_match_is_selected_after_typing()
    {
        var shown = PickerFilter.Apply(Entries("main", "develop", "dev/tools"), "dev");

        Assert.Equal(0, PickerFilter.Keep(shown, previous: -1));
        Assert.Equal(1, shown[PickerFilter.Keep(shown, previous: -1)]);
    }

    [Fact]
    public void The_selected_entry_stays_selected_when_it_still_matches()
    {
        var shown = PickerFilter.Apply(Entries("main", "develop", "dev/tools"), "dev");

        Assert.Equal(1, PickerFilter.Keep(shown, previous: 2));
    }

    [Fact]
    public void Nothing_is_selected_when_nothing_matches()
    {
        Assert.Equal(-1, PickerFilter.Keep([], previous: 0));
    }

    [Fact]
    public void A_printable_key_is_appended_to_the_filter()
    {
        Assert.Equal("fe", PickerFilter.Edit("f", Key.E));
        Assert.Equal("F", PickerFilter.Edit(string.Empty, Key.F.WithShift));
        Assert.Equal("a/", PickerFilter.Edit("a", (Key)'/'));
    }

    [Fact]
    public void Backspace_removes_the_last_typed_character()
    {
        Assert.Equal("fe", PickerFilter.Edit("fea", Key.Backspace));
    }

    [Fact]
    public void Keys_that_are_not_text_leave_the_filter_alone()
    {
        Assert.Null(PickerFilter.Edit("fe", Key.Enter));
        Assert.Null(PickerFilter.Edit("fe", Key.CursorDown));
        Assert.Null(PickerFilter.Edit("fe", Key.Esc));
        Assert.Null(PickerFilter.Edit("fe", Key.N.WithCtrl));
    }

    [Fact]
    public void Backspace_on_an_empty_filter_is_left_for_going_back()
    {
        Assert.Null(PickerFilter.Edit(string.Empty, Key.Backspace));
    }

    [Fact]
    public void A_refresh_appends_new_entries_after_the_known_ones()
    {
        var merged = PickerFilter.Merge(Entries("main", "origin/a"), Entries("main", "origin/b", "origin/a"));

        Assert.Equal(["main", "origin/a", "origin/b"], merged.Select(e => e.Label));
    }

    [Fact]
    public void A_refresh_keeps_known_entries_at_their_index_so_the_selection_survives()
    {
        var known = Entries("main", "develop");
        var merged = PickerFilter.Merge(known, Entries("origin/new", "develop", "main"));

        Assert.Equal(known, merged.Take(2));
    }

    [Fact]
    public void A_refresh_with_nothing_new_changes_nothing()
    {
        var known = Entries("main", "develop");

        Assert.Equal(known, PickerFilter.Merge(known, Entries("develop")));
    }
}
