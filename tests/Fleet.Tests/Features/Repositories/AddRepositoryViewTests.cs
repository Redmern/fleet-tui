using Fleet.Features.Repositories.AddRepository;
using Fleet.Shared.Settings.Enums;
using Fleet.Ui;
using Fleet.Ui.Constants;

namespace Fleet.Tests.Features.Repositories;

public sealed class AddRepositoryViewTests : IDisposable
{
    public AddRepositoryViewTests() => FleetButtonHints.Reset();

    public void Dispose() => FleetButtonHints.Reset();

    private static IReadOnlyList<FleetPillButton> Buttons() => AddRepositoryView.Buttons(() => { }, () => { });

    [Fact]
    public void The_title_leads_with_the_add_repository_icon() =>
        Assert.Equal($"{FleetIcons.AddRepo} Add repository", AddRepositoryView.Title);

    [Fact]
    public void Every_field_row_leads_with_its_icon_when_cloning()
    {
        var rows = AddRepositoryView.Fields(cloning: true, "fleet", "git@x:fleet.git", "main");

        Assert.Equal(
            [FleetIcons.Clone, FleetIcons.RepoName, FleetIcons.Url, FleetIcons.Branch],
            rows.Select(r => r.Spans[0].Text));
        Assert.All(rows, r => Assert.Equal(FleetTones.Icon, r.Spans[0].Tone));
    }

    [Fact]
    public void A_new_repository_has_no_url_row()
    {
        var rows = AddRepositoryView.Fields(cloning: false, "fleet", string.Empty, "main");

        Assert.Equal(
            [FleetIcons.Clone, FleetIcons.RepoName, FleetIcons.Branch],
            rows.Select(r => r.Spans[0].Text));
    }

    [Fact]
    public void Field_rows_keep_their_labels_values_and_placeholders()
    {
        var rows = AddRepositoryView.Fields(cloning: true, string.Empty, string.Empty, string.Empty);

        Assert.Equal(
        [
            $"{FleetIcons.Clone} Clone:   from a URL",
            $"{FleetIcons.RepoName} Name:    (required)",
            $"{FleetIcons.Url} URL:     (required)",
            $"{FleetIcons.Branch} Branch:  (default)",
        ],
            rows.Select(r => r.Text));
        Assert.EndsWith("new repository", AddRepositoryView.Fields(false, "x", "", "main")[0].Text);
    }

    [Fact]
    public void Add_repository_has_an_icon_and_the_a_key()
    {
        var add = Buttons()[0];

        Assert.Equal(("Add repository", FleetIcons.AddRepo, "a"), (add.Label, add.Icon, add.Mnemonic));
    }

    [Fact]
    public void Cancel_has_an_icon_and_the_esc_key()
    {
        var cancel = Buttons()[1];

        Assert.Equal(("Cancel", FleetIcons.Close, "Esc"), (cancel.Label, cancel.Icon, cancel.Mnemonic));
    }

    [Fact]
    public void Each_button_shows_an_icon_and_a_key_chip()
    {
        foreach (var button in Buttons())
        {
            var row = button.Row(ButtonHints.None);

            Assert.Contains(button.Icon, row.Text);
            Assert.Contains(row.Spans, s => s.Tone == FleetTones.ChipKey);
        }
    }

    [Theory]
    [InlineData(ButtonHints.None)]
    [InlineData(ButtonHints.Tooltips)]
    [InlineData(ButtonHints.Text)]
    public void The_button_names_show_in_every_hints_mode(ButtonHints hints)
    {
        Assert.Contains("Add repository", Buttons()[0].Row(hints).Text);
        Assert.Contains("Cancel", Buttons()[1].Row(hints).Text);
    }

    [Fact]
    public void The_buttons_run_add_and_cancel()
    {
        var ran = new List<string>();
        var buttons = AddRepositoryView.Buttons(() => ran.Add("add"), () => ran.Add("cancel"));

        buttons[0].Activate();
        buttons[1].Activate();

        Assert.Equal(["add", "cancel"], ran);
    }

    [Fact]
    public void No_button_key_clashes_with_the_list_motions_or_dialog_keys()
    {
        foreach (var button in Buttons())
        {
            Assert.False(button.Clashes(Keymap.Default), button.Mnemonic);
            Assert.False(button.Answers(FleetKeys.Back), button.Mnemonic);
        }
    }

    [Fact]
    public void The_add_key_is_not_a_default_list_motion()
    {
        var a = Buttons()[0];

        Assert.True(a.Answers(Terminal.Gui.Input.Key.A));
        Assert.False(a.Clashes(Keymap.Default));
    }
}
