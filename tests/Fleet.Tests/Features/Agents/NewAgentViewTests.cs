using Fleet.Features.Agents.NewAgent;
using Fleet.Shared.Settings.Enums;
using Fleet.Ui;
using Fleet.Ui.Constants;

namespace Fleet.Tests.Features.Agents;

public sealed class NewAgentViewTests
{
    [Fact]
    public void The_title_leads_with_the_new_agent_icon() =>
        Assert.Equal($"{FleetIcons.NewAgent} New agent", NewAgentView.Title);

    [Fact]
    public void Every_field_row_leads_with_its_icon()
    {
        var rows = NewAgentView.Fields("fleet", "feat/x", "main", "claude");

        Assert.Equal(
            [FleetIcons.Repo, FleetIcons.Branch, FleetIcons.Base, FleetIcons.Harness],
            rows.Select(r => r.Spans[0].Text));
        Assert.All(rows, r => Assert.Equal(FleetTones.Icon, r.Spans[0].Tone));
    }

    [Fact]
    public void A_field_row_keeps_its_label_column_and_value()
    {
        var row = NewAgentView.Fields("fleet", "feat/x", "main", "claude")[1];

        Assert.Equal($"{FleetIcons.Branch} Branch:  feat/x", row.Text);
    }

    [Fact]
    public void Create_agent_has_an_icon_and_the_c_key()
    {
        var create = NewAgentView.Buttons(() => { }, () => { })[0];

        Assert.Equal(("Create agent", FleetIcons.Create, "c"), (create.Label, create.Icon, create.Mnemonic));
    }

    [Fact]
    public void Cancel_has_an_icon_and_the_esc_key()
    {
        var cancel = NewAgentView.Buttons(() => { }, () => { })[1];

        Assert.Equal(("Cancel", FleetIcons.Close, "Esc"), (cancel.Label, cancel.Icon, cancel.Mnemonic));
    }

    [Fact]
    public void Each_button_shows_a_key_chip()
    {
        foreach (var button in NewAgentView.Buttons(() => { }, () => { }))
        {
            Assert.Contains(button.Row(ButtonHints.None).Spans, s => s.Tone == FleetTones.ChipKey);
        }
    }

    [Fact]
    public void The_buttons_run_create_and_cancel()
    {
        var ran = new List<string>();
        var buttons = NewAgentView.Buttons(() => ran.Add("create"), () => ran.Add("cancel"));

        buttons[0].Activate();
        buttons[1].Activate();

        Assert.Equal(["create", "cancel"], ran);
    }

    [Fact]
    public void No_button_key_clashes_with_the_list_motions()
    {
        foreach (var button in NewAgentView.Buttons(() => { }, () => { }))
        {
            Assert.False(button.Clashes(Keymap.Default), button.Mnemonic);
        }
    }
}
