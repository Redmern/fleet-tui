using Fleet.Features.Orchestrations.Dispatch;
using Fleet.Shared.Settings.Enums;
using Fleet.Ui;
using Fleet.Ui.Constants;
using Terminal.Gui.Input;

namespace Fleet.Tests.Features.Orchestrations;

public sealed class NewSubViewTests
{
    [Fact]
    public void The_title_leads_with_the_new_sub_icon() =>
        Assert.Equal($"{FleetIcons.NewSub} New sub-orchestrator", NewSubView.Title);

    [Fact]
    public void The_task_row_leads_with_the_task_icon()
    {
        var row = NewSubView.Fields("fix the build", 0)[0];

        Assert.Equal(FleetIcons.Task, row.Spans[0].Text);
        Assert.Equal(FleetTones.Icon, row.Spans[0].Tone);
        Assert.Equal($"{FleetIcons.Task} Task:    fix the build", row.Text);
    }

    [Fact]
    public void An_empty_task_shows_a_hint_instead_of_nothing() =>
        Assert.Equal(
            $"{FleetIcons.Task} Task:    {NewSubView.EmptyTask}",
            NewSubView.Fields("", 0)[0].Text);

    [Fact]
    public void Without_history_there_is_no_past_task_row() =>
        Assert.Single(NewSubView.Fields("x", 0));

    [Fact]
    public void With_history_a_past_task_row_follows_the_task_with_its_icon()
    {
        var rows = NewSubView.Fields("x", 3);

        Assert.Equal(2, rows.Count);
        Assert.Equal(FleetIcons.History, rows[1].Spans[0].Text);
        Assert.Contains("3 earlier tasks", rows[1].Text);
    }

    [Fact]
    public void Dispatch_has_an_icon_and_the_d_key()
    {
        var dispatch = NewSubView.Buttons(() => { }, () => { })[0];

        Assert.Equal(("Dispatch", FleetIcons.Dispatch, "d"), (dispatch.Label, dispatch.Icon, dispatch.Mnemonic));
    }

    [Fact]
    public void Cancel_has_an_icon_and_the_esc_key()
    {
        var cancel = NewSubView.Buttons(() => { }, () => { })[1];

        Assert.Equal(("Cancel", FleetIcons.Close, "Esc"), (cancel.Label, cancel.Icon, cancel.Mnemonic));
    }

    [Fact]
    public void Each_button_shows_a_key_chip()
    {
        foreach (var button in NewSubView.Buttons(() => { }, () => { }))
        {
            Assert.Contains(button.Row(ButtonHints.None).Spans, s => s.Tone == FleetTones.ChipKey);
        }
    }

    [Fact]
    public void With_text_hints_the_buttons_show_their_names()
    {
        foreach (var button in NewSubView.Buttons(() => { }, () => { }))
        {
            Assert.Contains(button.Label, button.Row(ButtonHints.Text).Text);
        }
    }

    [Fact]
    public void The_buttons_run_dispatch_and_cancel()
    {
        var ran = new List<string>();
        var buttons = NewSubView.Buttons(() => ran.Add("dispatch"), () => ran.Add("cancel"));

        buttons[0].Activate();
        buttons[1].Activate();

        Assert.Equal(["dispatch", "cancel"], ran);
    }

    [Fact]
    public void The_d_key_answers_dispatch()
    {
        var buttons = NewSubView.Buttons(() => { }, () => { });

        Assert.True(buttons[0].Answers(Key.D));
        Assert.False(buttons[1].Answers(Key.D));
    }

    [Fact]
    public void No_button_key_clashes_with_the_list_motions()
    {
        foreach (var button in NewSubView.Buttons(() => { }, () => { }))
        {
            Assert.False(button.Clashes(Keymap.Default), button.Mnemonic);
        }
    }

    [Fact]
    public void The_d_key_is_not_page_down() =>
        Assert.NotEqual(Keymap.Default.KeyFor(Fleet.Shared.Keymap.Enums.FleetAction.PageDown), Key.D);

    [Fact]
    public void Ctrl_enter_submits_the_task_while_typing()
    {
        Assert.True(NewSubView.SubmitsWhileTyping(Key.Enter.WithCtrl));
        Assert.False(NewSubView.SubmitsWhileTyping(Key.Enter));
        Assert.False(NewSubView.SubmitsWhileTyping(Key.D));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("fix it", true)]
    public void Only_a_non_blank_task_can_be_dispatched(string task, bool ready) =>
        Assert.Equal(ready, NewSubView.Dispatchable(task));
}
