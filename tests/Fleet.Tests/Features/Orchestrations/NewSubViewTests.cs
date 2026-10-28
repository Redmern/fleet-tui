using Fleet.Features.Orchestrations.Dispatch;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Settings.Enums;
using Fleet.Ui;
using Fleet.Ui.Constants;
using Terminal.Gui.Input;

namespace Fleet.Tests.Features.Orchestrations;

public sealed class NewSubViewTests
{
    private static readonly IReadOnlyList<string> Past =
        ["fix the build", "Add a Build badge", "write the release notes"];

    private static IReadOnlyList<FleetPillButton> Pills(List<string>? ran = null) =>
        NewSubView.Pills(() => ran?.Add("dispatch"), () => ran?.Add("cancel"));

    [Fact]
    public void The_title_leads_with_the_new_sub_icon() =>
        Assert.Equal($"{FleetIcons.NewSub} New sub-orchestrator", NewSubView.Title);

    [Fact]
    public void The_task_header_leads_with_the_task_icon_and_shows_its_jump_chip()
    {
        var row = NewSubView.TaskHeader("t");

        Assert.Equal(FleetIcons.Task, row.Spans[0].Text);
        Assert.Equal(FleetTones.Icon, row.Spans[0].Tone);
        Assert.Contains("Task", row.Text);
        Assert.Contains(row.Trailing!, s => s.Tone == FleetTones.ChipKey && s.Text.Contains('t'));
    }

    [Theory]
    [InlineData(0, "no earlier tasks")]
    [InlineData(1, "1 earlier task")]
    [InlineData(3, "3 earlier tasks")]
    public void The_past_header_counts_the_earlier_tasks(int count, string shown)
    {
        var row = NewSubView.PastHeader(count, "p");

        Assert.Equal(FleetIcons.History, row.Spans[0].Text);
        Assert.Contains(shown, row.Text);
        Assert.Contains(row.Trailing!, s => s.Tone == FleetTones.ChipKey && s.Text.Contains('p'));
    }

    [Fact]
    public void The_task_and_past_jump_keys_are_t_and_p() =>
        Assert.Equal(["t", "p"], NewSubView.Keys(Keymap.Default, Pills()));

    [Fact]
    public void The_jump_keys_avoid_the_list_motions_and_the_dispatch_key()
    {
        var motions = new[] { FleetAction.MoveDown, FleetAction.MoveUp, FleetAction.MoveFirst, FleetAction.MoveLast }
            .Select(Keymap.Default.TextFor)
            .ToHashSet();

        foreach (var key in NewSubView.Keys(Keymap.Default, Pills()))
        {
            Assert.DoesNotContain(key, motions);
            Assert.NotEqual("d", key);
        }
    }

    [Fact]
    public void The_jump_keys_map_to_task_past_dispatch_and_cancel()
    {
        var pills = Pills();
        var keys = NewSubView.Keys(Keymap.Default, pills);

        Assert.Equal(NewSubView.TaskSlot, FleetCard.Jump(new Key('t'), keys, pills));
        Assert.Equal(NewSubView.PastSlot, FleetCard.Jump(new Key('p'), keys, pills));
        Assert.Equal(2, FleetCard.Jump(Key.D, keys, pills));
        Assert.Equal(3, FleetCard.Jump(Key.Esc, keys, pills));
    }

    [Fact]
    public void Dispatch_has_an_icon_and_the_d_key()
    {
        var dispatch = Pills()[0];

        Assert.Equal(("Dispatch", FleetIcons.Dispatch, "d"), (dispatch.Label, dispatch.Icon, dispatch.Mnemonic));
    }

    [Fact]
    public void Cancel_has_an_icon_and_the_esc_key()
    {
        var cancel = Pills()[1];

        Assert.Equal(("Cancel", FleetIcons.Close, "Esc"), (cancel.Label, cancel.Icon, cancel.Mnemonic));
    }

    [Fact]
    public void Each_button_shows_a_key_chip()
    {
        foreach (var button in Pills())
        {
            Assert.Contains(button.Row(ButtonHints.None).Spans, s => s.Tone == FleetTones.ChipKey);
        }
    }

    [Fact]
    public void The_buttons_run_dispatch_and_cancel()
    {
        var ran = new List<string>();
        var buttons = Pills(ran);

        buttons[0].Activate();
        buttons[1].Activate();

        Assert.Equal(["dispatch", "cancel"], ran);
    }

    [Fact]
    public void No_button_key_clashes_with_the_list_motions()
    {
        foreach (var button in Pills())
        {
            Assert.False(button.Clashes(Keymap.Default), button.Mnemonic);
        }
    }

    [Fact]
    public void With_nothing_typed_every_past_task_is_listed() =>
        Assert.Equal([0, 1, 2], NewSubView.Shown(Past, ""));

    [Fact]
    public void Typing_filters_the_past_tasks_case_insensitively() =>
        Assert.Equal([0, 1], NewSubView.Shown(Past, "BUILD"));

    [Fact]
    public void A_multi_line_task_filters_as_its_one_line_form() =>
        Assert.Equal([0], NewSubView.Shown(Past, "fix the\nbuild"));

    [Fact]
    public void Past_rows_lead_with_the_history_icon()
    {
        var rows = NewSubView.PastRows(Past, [0, 2], 40);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(FleetIcons.History, r.Spans[0].Text));
        Assert.Contains("write the release notes", rows[1].Text);
    }

    [Fact]
    public void A_long_past_task_is_cut_to_one_row()
    {
        var rows = NewSubView.PastRows([new string('x', 200)], [0], 30);

        Assert.True(rows[0].Text.Length <= 30, rows[0].Text);
        Assert.EndsWith("…", rows[0].Text);
    }

    [Fact]
    public void Without_history_the_list_says_so()
    {
        var row = Assert.Single(NewSubView.PastRows([], [], 40));

        Assert.Equal(FleetTones.Muted, row.Spans[0].Tone);
        Assert.Contains(NewSubView.NoPast, row.Text);
    }

    [Fact]
    public void When_the_filter_matches_nothing_the_list_says_so()
    {
        var row = Assert.Single(NewSubView.PastRows(Past, [], 40));

        Assert.Contains(NewSubView.NoMatch, row.Text);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(" \n\t\n ", false)]
    [InlineData("fix it", true)]
    [InlineData("fix it\nthen test", true)]
    public void Only_a_non_blank_task_can_be_dispatched(string task, bool ready) =>
        Assert.Equal(ready, NewSubView.Dispatchable(task));

    [Theory]
    [InlineData("")]
    [InlineData("  \n  ")]
    public void A_blank_task_gives_an_inline_error(string task) =>
        Assert.Equal(NewSubView.EmptyTask, NewSubView.Problem(task));

    [Fact]
    public void A_real_task_has_no_error() =>
        Assert.Null(NewSubView.Problem("fix it"));

    [Fact]
    public void A_multi_line_task_is_dispatched_with_its_lines_intact() =>
        Assert.Equal("fix the build\n\nthen run the tests", NewSubView.Dispatched("  fix the build\n\nthen run the tests\n"));
}
